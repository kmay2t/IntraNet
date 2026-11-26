using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

/// Reglas de negocio del almacén de equipos: existencias, entradas, salidas, traslados y su
/// historial. Las existencias se leen y escriben SIEMPRE sobre el catálogo de equipos
/// (`IInventarioStore`, colección `equipos`), que es la única fuente de verdad del stock; los
/// movimientos (`IAlmacenStore`) son el documento y la trazabilidad de cada cambio.
///
/// Ciclo de vida:
///   Entrada/Salida: Borrador → Confirmado (aplica existencias) | Cancelado | Rechazado.
///   Traslado:       Borrador → EnTransito (descuenta origen) → Recibido (suma destino)
///                   | Cancelado/Rechazado (reversa al origen).
/// Ninguna operación terminal mueve existencias dos veces y las disminuciones se validan todas
/// antes de aplicar la primera (no quedan cambios parciales ni stock negativo).
public class AlmacenService(IAlmacenStore almacen, IInventarioStore equipos, IAuditLog bitacora, GestionCatalogos catalogos)
{
    public bool Tiene(GestionService.Sesion s, string clave) => s.Puede(clave);

    /// El almacén «propio» de un movimiento: el destino en una entrada (ahí llega el stock) y el
    /// origen en salidas y traslados (de ahí sale). Es el lado que tiene que estar dentro de la región
    /// de quien lo registra o lo confirma.
    private static string AlmacenPropio(string tipo, string? origen, string? destino) =>
        tipo == TiposMovimiento.Entrada ? destino ?? "" : origen ?? "";

    /// Si el movimiento le toca a la región del usuario. Basta con que **uno** de los dos almacenes
    /// sea de su región: así un traslado que sale de ella sigue siendo operable por quien está en ella
    /// (es su stock), pero un movimiento entre dos hospitales ajenos desaparece de su pantalla. Con
    /// «ver todas las regiones», o cuando el movimiento no indica hospital, siempre le toca.
    private bool LeToca(GestionService.Sesion s, params string?[] hospitales)
    {
        if (catalogos.VeTodasRegiones(s.Usuario)) return true;
        var escritos = hospitales.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
        if (escritos.Count == 0) return true;
        return escritos.Any(h => catalogos.ErrorRegionDe(s.Usuario, h) is null);
    }

    // ---- existencias (catálogo de equipos como lotes de stock) ----

    public static int CantidadDe(ItemInventario lote) =>
        int.TryParse(lote.Valor("cantidad"), out var c) && c > 0 ? c : 0;

    /// Lotes de stock visibles: filtrados por hospital y texto (n.º inventario, serie, nombre,
    /// marca, modelo, categoría, servicio o ubicación). Los lotes con **cantidad 0** no se
    /// listan: en cuanto un equipo se queda sin existencias desaparece de la pantalla y del Excel
    /// (el registro y su historial de movimientos se conservan; si vuelve a entrar stock, reaparece).
    public IReadOnlyList<ItemInventario> Existencias(GestionService.Sesion s, string? hospital, string? texto)
    {
        if (!Tiene(s, CatalogoPermisos.AlmacenConsultar)) return Array.Empty<ItemInventario>();
        var h = Hospitales.Normalizar(hospital, permiteGeneral: true);
        // Búsqueda sin acentos ni mayúsculas: "muno" encuentra "MUÑOZ", "cranio" encuentra "CRÁNEO".
        var t = Texto.Normalizar(texto);
        return catalogos.FiltraPorRegion(s.Usuario, equipos.List(InventarioCatalogo.Equipos), i => i.Hospital)
            .Where(i => CantidadDe(i) > 0)
            .Where(i => h.Length == 0 || string.Equals(i.Hospital, h, StringComparison.Ordinal))
            .Where(i => t.Length == 0
                || Texto.Normalizar(i.Valor("numInventario")).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(i.Valor("serie")).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(i.Valor("nombre")).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(i.Valor("marca")).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(i.Valor("modelo")).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(i.Valor("categoria")).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(i.Valor("servicio")).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(i.Valor("ubicacion")).Contains(t, StringComparison.Ordinal))
            .ToList();
    }

    /// ¿Dos líneas describen el mismo equipo físico? Por n.º de inventario, por serie o, en su
    /// defecto, por nombre+marca+modelo. Evita duplicar una unidad física en dos lotes.
    public static bool MismoEquipo(ItemInventario lote, MovimientoDetalle d)
    {
        if (!string.IsNullOrWhiteSpace(d.NumInventario) &&
            string.Equals(lote.Valor("numInventario"), d.NumInventario, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(d.Serie) &&
            string.Equals(lote.Valor("serie"), d.Serie, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(lote.Valor("nombre"), d.Nombre, StringComparison.OrdinalIgnoreCase)
            && string.Equals(lote.Valor("marca"), d.Marca, StringComparison.OrdinalIgnoreCase)
            && string.Equals(lote.Valor("modelo"), d.Modelo, StringComparison.OrdinalIgnoreCase);
    }

    private ItemInventario? LoteEn(string hospital, string ubicacion, MovimientoDetalle d) =>
        equipos.List(InventarioCatalogo.Equipos).FirstOrDefault(i =>
            string.Equals(i.Hospital, hospital, StringComparison.Ordinal)
            && string.Equals(i.Valor("ubicacion"), ubicacion, StringComparison.OrdinalIgnoreCase)
            && MismoEquipo(i, d));

    private static ItemInventario ConCantidad(ItemInventario lote, int cantidad) =>
        lote with { Datos = SetCampo(lote.Datos, "cantidad", cantidad.ToString()), Actualizado = DateTime.Now };

    private static ItemInventario ConEstado(ItemInventario lote, string estado) =>
        lote with { Datos = SetCampo(lote.Datos, "estado", estado), Actualizado = DateTime.Now };

    private static IReadOnlyDictionary<string, string> SetCampo(IReadOnlyDictionary<string, string> datos, string clave, string valor)
    {
        var d = new Dictionary<string, string>(datos, StringComparer.Ordinal) { [clave] = valor };
        return d;
    }

    // ---- consulta de movimientos ----

    public IReadOnlyList<MovimientoInventario> Movimientos(
        GestionService.Sesion s, string? tipo, string? estado, string? hospital, DateTime? desde, DateTime? hasta, string? texto)
    {
        if (!Tiene(s, CatalogoPermisos.AlmacenConsultar)) return Array.Empty<MovimientoInventario>();
        var t = Texto.Normalizar(texto);
        return almacen.List()
            .Where(m => LeToca(s, m.HospitalOrigen, m.HospitalDestino))
            .Where(m => string.IsNullOrEmpty(tipo) || m.Tipo == tipo)
            .Where(m => string.IsNullOrEmpty(estado) || m.Estado == estado)
            .Where(m => string.IsNullOrEmpty(hospital) || m.HospitalOrigen == hospital || m.HospitalDestino == hospital)
            .Where(m => desde is null || m.Fecha.Date >= desde.Value.Date)
            .Where(m => hasta is null || m.Fecha.Date <= hasta.Value.Date)
            .Where(m => t.Length == 0
                || Texto.Normalizar(m.Folio).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(m.Motivo).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(m.ResponsableEntrega).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(m.ResponsableRecibe).Contains(t, StringComparison.Ordinal)
                || Texto.Normalizar(m.UsuarioRegistra).Contains(t, StringComparison.Ordinal)
                || m.Detalles.Any(d => Texto.Normalizar(d.Etiqueta).Contains(t, StringComparison.Ordinal)))
            .ToList();
    }

    public MovimientoInventario? Find(long id) => almacen.Find(id);

    // ---- alta ----

    /// Registra un movimiento en Borrador con folio único. Valida la cabecera, el permiso y, en
    /// entradas, que ninguna serie repetida cree una unidad física duplicada.
    public (int Status, string? Error, MovimientoInventario? Item) Crear(
        GestionService.Sesion s, MovimientoInventario borrador, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.AlmacenRegistrar))
            return (403, "No tienes permiso para registrar movimientos del almacén.", null);
        if (AlmacenRules.CabeceraError(borrador) is string e) return (400, e, null);
        if (catalogos.ErrorRegionDe(s.Usuario, AlmacenPropio(borrador.Tipo, borrador.HospitalOrigen, borrador.HospitalDestino)) is string eR)
            return (400, eR, null);

        // Una entrada no debe duplicar una unidad física ya existente (misma serie o n.º inventario).
        if (borrador.Tipo == TiposMovimiento.Entrada)
        {
            var existentes = equipos.List(InventarioCatalogo.Equipos);
            foreach (var d in borrador.Detalles)
            {
                // El duplicado debe buscarse ya filtrado por el almacén de destino: la misma serie
                // puede existir en otros hospitales/almacenes sin que eso sea un duplicado físico.
                var dup = existentes.FirstOrDefault(x =>
                    x.Hospital == borrador.HospitalDestino
                    && string.Equals(x.Valor("ubicacion"), borrador.UbicacionDestino, StringComparison.OrdinalIgnoreCase)
                    && ((!string.IsNullOrWhiteSpace(d.Serie) && string.Equals(x.Valor("serie"), d.Serie, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(d.NumInventario) && string.Equals(x.Valor("numInventario"), d.NumInventario, StringComparison.OrdinalIgnoreCase))));
                // Si el "duplicado" es exactamente el lote al que apunta la línea (EquipoId),
                // no es una unidad física nueva: es un incremento sobre ese lote → se permite.
                if (dup is not null && dup.Id != d.EquipoId)
                    return (400, $"El equipo {d.Etiqueta} ya existe en ese almacén; registra una entrada sobre el lote existente.", null);
            }
        }

        var folio = AlmacenStoreBase.FolioPublico(borrador.Tipo, almacen.SiguienteFolio(borrador.Tipo));
        var item = borrador with
        {
            Id = 0,
            Folio = folio,
            Estado = EstadosMovimiento.Borrador,
            UsuarioRegistra = s.Usuario,
            Creado = DateTime.Now,
            Modificado = null,
            FechaAutorizacion = null,
            UsuarioAutoriza = "",
        };
        var (status, error, guardado) = almacen.Create(item);
        if (status != 201 || guardado is null) return (status, error, null);
        bitacora.Register(s.Usuario, AuditEvents.CrearMovimiento, $"{guardado.Folio} · {guardado.Tipo} · {guardado.Motivo}", ip);
        return (201, null, guardado);
    }

    // ---- transiciones ----

    /// Confirma un movimiento en Borrador: Entrada/Salida aplican existencias y quedan
    /// Confirmados; un Traslado descuenta el origen y queda EnTransito hasta su recepción.
    public (int Status, string? Error, MovimientoInventario? Item) Confirmar(GestionService.Sesion s, long id, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.AlmacenRegistrar))
            return (403, "No tienes permiso para confirmar movimientos del almacén.", null);
        var m = almacen.Find(id);
        if (m is null) return (404, "El movimiento no existe.", null);
        if (m.Estado != EstadosMovimiento.Borrador) return (400, "Solo se confirma un movimiento en Borrador.", null);
        // Se vuelve a chequear la región al confirmar: el borrador pudo haberlo escrito otra cuenta
        // (o una región anterior) y el stock que se mueve ahora es responsabilidad de quien confirma.
        if (catalogos.ErrorRegionDe(s.Usuario, AlmacenPropio(m.Tipo, m.HospitalOrigen, m.HospitalDestino)) is string eR)
            return (400, eR, null);

        if (m.Tipo == TiposMovimiento.Traslado)
        {
            if (Disminuir(s, m.HospitalOrigen, m.UbicacionOrigen, m.Detalles, marcarEnTransito: true) is string eTr)
                return (400, eTr, null);
            return GuardarEstado(s, m, EstadosMovimiento.EnTransito, AuditEvents.ConfirmarMovimiento, ip);
        }

        // Entrada / Salida: valida todo antes de tocar el stock.
        if (m.Tipo == TiposMovimiento.Salida &&
            Disminuir(s, m.HospitalOrigen, m.UbicacionOrigen, m.Detalles, marcarEnTransito: false) is string eSal)
            return (400, eSal, null);
        if (m.Tipo == TiposMovimiento.Entrada) Aumentar(m.HospitalDestino, m.UbicacionDestino, m.Detalles, s);
        return GuardarEstado(s, m, EstadosMovimiento.Confirmado, AuditEvents.ConfirmarMovimiento, ip);
    }

    /// Confirma la recepción de un traslado EnTransito: suma las existencias en el destino.
    public (int Status, string? Error, MovimientoInventario? Item) Recibir(GestionService.Sesion s, long id, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.AlmacenRegistrar))
            return (403, "No tienes permiso para recibir traslados.", null);
        var m = almacen.Find(id);
        if (m is null) return (404, "El movimiento no existe.", null);
        if (m.Tipo != TiposMovimiento.Traslado) return (400, "Solo los traslados se reciben.", null);
        if (m.Estado != EstadosMovimiento.EnTransito) return (400, "El traslado no está en tránsito.", null);
        // Recibir es acción del destino: la región que tiene que cubrir el hospital receptor.
        if (catalogos.ErrorRegionDe(s.Usuario, m.HospitalDestino) is string eR) return (400, eR, null);

        Aumentar(m.HospitalDestino, m.UbicacionDestino, m.Detalles, s);
        return GuardarEstado(s, m, EstadosMovimiento.Recibido, AuditEvents.RecibirMovimiento, ip);
    }

    /// Cancela o rechaza un movimiento. En Borrador no mueve stock; un traslado EnTransito
    /// devuelve las existencias al origen. Requiere `almacen.autorizar` salvo cancelar un
    /// Borrador propio.
    public (int Status, string? Error, MovimientoInventario? Item) Cancelar(
        GestionService.Sesion s, long id, bool rechazo, string? ip)
    {
        var m = almacen.Find(id);
        if (m is null) return (404, "El movimiento no existe.", null);
        if (EstadosMovimiento.Terminal(m.Estado)) return (400, "El movimiento ya está cerrado.", null);

        var esPropio = string.Equals(m.UsuarioRegistra, s.Usuario, StringComparison.OrdinalIgnoreCase);
        var necesitaAutorizar = m.Estado == EstadosMovimiento.EnTransito || !esPropio;
        if (necesitaAutorizar && !Tiene(s, CatalogoPermisos.AlmacenAutorizar))
            return (403, "Solo quien tiene permiso de autorización puede cancelar/rechazar este movimiento.", null);
        if (!necesitaAutorizar && !Tiene(s, CatalogoPermisos.AlmacenRegistrar))
            return (403, "No tienes permiso para cancelar movimientos.", null);
        // Cancelar o rechazar le toca a cualquiera de los dos lados: el que envía retira su pedido y
        // el que recibe lo rechaza. Lo que no puede es meterse en un movimiento entre dos ajenos.
        if (!LeToca(s, m.HospitalOrigen, m.HospitalDestino))
            return (400, "El movimiento no pertenece a la región asignada a su usuario.", null);

        if (m.Estado == EstadosMovimiento.EnTransito)
            Reversar(m.HospitalOrigen, m.UbicacionOrigen, m.Detalles);

        var estado = rechazo ? EstadosMovimiento.Rechazado : EstadosMovimiento.Cancelado;
        return GuardarEstado(s, m, estado, AuditEvents.CancelarMovimiento, ip);
    }

    // ---- aplicación de existencias ----

    /// Descuenta stock del origen. Valida TODAS las líneas antes de aplicar la primera para no
    /// dejar cambios parciales ni existencias negativas. Devuelve null si aplicó, o el error.
    private string? Disminuir(GestionService.Sesion s, string hospital, string ubicacion,
        IReadOnlyList<MovimientoDetalle> detalles, bool marcarEnTransito)
    {
        var lotes = new List<(ItemInventario Lote, int Nueva)>();
        foreach (var d in detalles)
        {
            var lote = equipos.Find(InventarioCatalogo.Equipos, d.EquipoId);
            if (lote is null) return $"El equipo {d.Etiqueta} no existe en el inventario.";
            if (!string.Equals(lote.Hospital, hospital, StringComparison.Ordinal)
                || !string.Equals(lote.Valor("ubicacion"), ubicacion, StringComparison.OrdinalIgnoreCase))
                return $"El equipo {d.Etiqueta} no pertenece al almacén de origen.";
            var disponible = CantidadDe(lote);
            if (disponible < d.Cantidad)
                return $"Stock insuficiente de {d.Etiqueta}: hay {disponible} y se piden {d.Cantidad}.";
            lotes.Add((lote, disponible - d.Cantidad));
        }
        foreach (var (lote, nueva) in lotes)
        {
            var actualizado = ConCantidad(lote, nueva);
            if (marcarEnTransito) actualizado = ConEstado(actualizado, nueva > 0 ? "En tránsito" : "Baja");
            equipos.Guardar(actualizado);
        }
        return null;
    }

    /// Suma stock en el destino: incrementa el lote existente o crea uno nuevo si el equipo aún
    /// no está dado de alta en ese almacén.
    private void Aumentar(string hospital, string ubicacion, IReadOnlyList<MovimientoDetalle> detalles, GestionService.Sesion s)
    {
        foreach (var d in detalles)
        {
            var lote = LoteEn(hospital, ubicacion, d) ?? equipos.Find(InventarioCatalogo.Equipos, d.EquipoId);
            if (lote is not null && string.Equals(lote.Hospital, hospital, StringComparison.Ordinal)
                && string.Equals(lote.Valor("ubicacion"), ubicacion, StringComparison.OrdinalIgnoreCase))
            {
                equipos.Guardar(ConEstado(ConCantidad(lote, CantidadDe(lote) + d.Cantidad), "Disponible"));
            }
            else
            {
                var datos = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["numInventario"] = d.NumInventario,
                    ["nombre"] = d.Nombre,
                    ["categoria"] = "Otro",
                    ["marca"] = d.Marca,
                    ["modelo"] = d.Modelo,
                    ["serie"] = d.Serie,
                    ["servicio"] = "",
                    ["ubicacion"] = ubicacion,
                    ["cantidad"] = d.Cantidad.ToString(),
                    ["estado"] = "Disponible",
                    ["fechaIngreso"] = DateTime.Now.ToString("yyyy-MM-dd"),
                    ["procedencia"] = "",
                    ["docEntrada"] = "",
                    ["notas"] = "",
                };
                equipos.Create(new ItemInventario(0, InventarioCatalogo.Equipos, hospital, datos, s.Usuario, DateTime.Now, null));
            }
        }
    }

    /// Devuelve al origen el stock de un traslado cancelado/rechazado.
    private void Reversar(string hospital, string ubicacion, IReadOnlyList<MovimientoDetalle> detalles)
    {
        foreach (var d in detalles)
        {
            var lote = equipos.Find(InventarioCatalogo.Equipos, d.EquipoId);
            if (lote is null) continue;
            var nueva = CantidadDe(lote) + d.Cantidad;
            var actualizado = ConCantidad(lote, nueva);
            // Al confirmar el traslado el origen quedó marcado "En tránsito" (parcial) o "Baja"
            // (cuando se movió todo). Como el stock regresa, vuelve a quedar disponible.
            var estado = lote.Valor("estado");
            var enTransito = string.Equals(estado, "En tránsito", StringComparison.OrdinalIgnoreCase)
                || string.Equals(estado, "Baja", StringComparison.OrdinalIgnoreCase);
            if (nueva > 0 && enTransito)
                actualizado = ConEstado(actualizado, "Disponible");
            equipos.Guardar(actualizado);
        }
    }

    private (int Status, string? Error, MovimientoInventario? Item) GuardarEstado(
        GestionService.Sesion s, MovimientoInventario m, string estado, string evento, string? ip)
    {
        var (status, error, guardado) = almacen.Guardar(m with
        {
            Estado = estado,
            Modificado = DateTime.Now,
            FechaAutorizacion = DateTime.Now,
            UsuarioAutoriza = s.Usuario,
        });
        if (status != 200 || guardado is null) return (status, error, null);
        bitacora.Register(s.Usuario, evento, $"{guardado.Folio} · {guardado.Tipo} → {estado}", ip);
        return (status, null, guardado);
    }
}
