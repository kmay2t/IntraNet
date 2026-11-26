using System.Security.Claims;
using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

/// Reglas de negocio del módulo Gestión (actividades, incidentes y tickets). Los endpoints de
/// Program.cs son delgados: validan sesión y delegan aquí. Toda operación importante deja rastro
/// en el historial de cambios del elemento y en la bitácora general.
public class GestionService(
    IActivityStore items,
    IReportStore reportes,
    IHistorialStore historial,
    IConfigStore config,
    IUserStore usuarios,
    IAuditLog bitacora,
    GestionCatalogos catalogos,

    // El almacén de evidencias (`evidencias`) solo guarda y lee renglones; las reglas de adjuntar —
    // permisos, extensiones, disco— viven en `EvidenciaService`, que aquí se llama `altaEvidencias`
    // para el alta que nace cerrada. No hay ciclo de inyección: `EvidenciaService` no conoce a esta
    // clase, solo a su `Sesion`.
    IEvidenciaStore evidencias,
    EvidenciaService altaEvidencias)
{
    /// Quién llama (sale de la cookie de sesión). `Permisos` es el conjunto **efectivo** del
    /// usuario (matriz del rol + ajustes por usuario), que `Program.cs` adjunta como claims `perm`
    /// en cada petición. Todas las comprobaciones de autorización consultan este conjunto, de modo
    /// que los roles personalizados y las excepciones por usuario funcionan en todo el módulo.
    public record Sesion(string Usuario, string Rol, IReadOnlySet<string> Permisos)
    {
        /// True solo para el rol protegido `Admin` (se conserva para la UI y la semántica de "jefe").
        public bool EsAdmin => string.Equals(Rol, Roles.Admin, StringComparison.OrdinalIgnoreCase);
        public bool Puede(string clave) => Permisos.Contains(clave);
        /// Ve todos los elementos (no solo los propios): el Administrador o quien tenga permiso de
        /// histórico o de supervisión por ingeniero.
        public bool VeTodo => EsAdmin
            || Permisos.Contains(CatalogoPermisos.HistoricoActividades)
            || Permisos.Contains(CatalogoPermisos.SupervisionIngeniero);
    }

    /// Tipo de claim con el que la cookie de sesión carga los permisos efectivos del usuario.
    public const string PermClaim = "perm";

    public static Sesion De(ClaimsPrincipal me)
    {
        var usuario = me.Identity?.Name ?? "";
        var rol = me.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
        var permisos = me.FindAll(PermClaim).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        return new Sesion(usuario, rol, permisos);
    }

    public bool Tiene(Sesion s, string clave) => s.Puede(clave);

    public int PeriodicidadPorDefecto() =>
        int.TryParse(config.Get(ConfigKeys.PeriodicidadReporteDias, "7"), out var d) && d > 0 ? d : 7;

    /// Interruptor administrativo "Habilitar fecha límite". Con `false` la fecha límite no se pide,
    /// no se guarda, no se valida y no marca elementos vencidos: es exclusivamente del Admin.
    public bool FechaLimiteHabilitada() =>
        !string.Equals(config.Get(ConfigKeys.FechaLimiteHabilitada, "true"), "false", StringComparison.OrdinalIgnoreCase);

    /// Solo fija la fecha límite quien tenga el permiso correspondiente y cuando está habilitada;
    /// si está deshabilitada, nadie la usa.
    private DateTime? FechaLimiteEfectiva(Sesion s, DateTime? fechaLimite) =>
        s.Puede(CatalogoPermisos.ConfiguracionFechaLimite) && FechaLimiteHabilitada() ? fechaLimite?.Date : null;

    public static bool EsDuenoOAdmin(Sesion s, Actividad a) =>
        s.VeTodo || string.Equals(a.Usuario, s.Usuario, StringComparison.OrdinalIgnoreCase);

    /// Lo que ve cada quién: quien tenga visibilidad global (Admin, histórico o supervisión) ve todo;
    /// el resto, lo que tiene asignado o lo que él creó (para dar seguimiento a tickets/incidentes
    /// registrados y aún sin asignar). Encima se quita lo que no sea de su región, cuando el Programador
    /// tenga asignada una (ver GestionCatalogos.FiltraPorRegion).
    public IReadOnlyList<Actividad> Visibles(Sesion s)
    {
        var lista = catalogos.FiltraPorRegion(s.Usuario, items.List(), a => a.Hospital);
        return ConComentarios(s.VeTodo
            ? lista
            : lista.Where(a =>
                string.Equals(a.Usuario, s.Usuario, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.CreadoPor, s.Usuario, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    public Actividad? Find(long id)
    {
        var item = items.Find(id);
        if (item is null) return null;
        return historial.UltimoComentario(id) is DateTime m ? item with { UltimoComentario = m } : item;
    }

    /// Adjunta a cada elemento el momento de su último comentario (sale del historial), que es lo que
    /// alimenta el recordatorio de seguimiento: a los 3 días sin escribir nada, la tarjeta y el
    /// calendario piden el avance (<see cref="Actividad.LeTocaReporte"/>).
    private IReadOnlyList<Actividad> ConComentarios(IReadOnlyList<Actividad> lista)
    {
        if (lista.Count == 0) return lista;
        var ultimos = historial.UltimosComentarios();
        return ultimos.Count == 0
            ? lista
            : lista.Select(a => ultimos.TryGetValue(a.Id, out var m) ? a with { UltimoComentario = m } : a).ToList();
    }

    // ---- creación ----

    /// <summary>Alta normal (la del cuerpo JSON), usada por el Administrador y por los incidentes y
    /// tickets de cualquiera: nacen `Pendiente`. **El Ingeniero no puede crear actividades por aquí**
    /// —«directamente al crear una actividad debe estar en completo, no puede quedar pendiente»—: su
    /// alta viaja en un solo envío multipart con el informe y la evidencia, y la hace
    /// <see cref="CrearYCerrada"/>, que es la única que entra con `exigirCierre` en false. Quien puede
    /// hacer la asignación formal decide además prioridad, fechas e ingeniero.</summary>
    public (int Status, string? Error, Actividad? Item) Crear(
        Sesion s, string tipo, string hospital, string titulo, string descripcion, string usuario,
        DateTime fecha, DateTime? fechaLimite, string prioridad, int periodicidadDias, long? relacionId, string? ip,
        bool exigirCierre = true)
    {
        if (!TiposItem.EsValido(tipo)) return (400, ActividadRules.TipoError(tipo), null);
        var permisoCrear = tipo switch
        {
            TiposItem.Ticket => CatalogoPermisos.CrearTicket,
            TiposItem.Incidente => CatalogoPermisos.CrearIncidente,
            _ => CatalogoPermisos.CrearActividad,
        };
        if (!Tiene(s, permisoCrear)) return (403, $"Tu rol no puede crear {tipo.ToLowerInvariant()}s.", null);
        if (ActividadRules.HospitalError(hospital) is string eH) return (400, eH, null);
        // El catálogo ya aceptó el hospital; falta que le toque a la región del usuario. Sin región
        // asignada (o con «ver todas») esto devuelve null y se comporta igual que antes.
        if (catalogos.ErrorRegionDe(s.Usuario, hospital) is string eR) return (400, eR, null);
        if (ActividadRules.TituloError(titulo) is string eT) return (400, eT, null);
        // La prioridad, la fecha límite y el ingeniero responsable son decisiones de la asignación
        // formal: quien no tiene `tickets.asignar` no las ve en «+ Nuevo» y, si alguien las manda por
        // otro camino, aquí se ignoran. El elemento queda en Media y sin fecha límite hasta que el
        // Administrador lo asigna.
        var puedeAsignar = Tiene(s, CatalogoPermisos.TicketsAsignar);
        // «El perfil de ingeniero solo puede subir las actividades del día de hoy»: quien no hace la
        // asignación formal registra lo que hizo **hoy**, ni hacia atrás ni por adelantado. El
        // Administrador (y cualquier rol con `tickets.asignar`) sí fecha elementos de otro día. Es solo
        // el **alta**: completar con evidencia una actividad suya que quedó pendiente de hace días
        // sigue permitido, porque recortar eso dejaría el rezago sin salida.
        if (!puedeAsignar && fecha.Date != DateTime.Today)
            return (400, "Solo puede registrar actividades del día de hoy.", null);
        // El Ingeniero no deja actividades pendientes: su alta nace completada con su evidencia, en un
        // solo envío. Si el cuerpo JSON llega sin poder de asignación formal y pide una Actividad, se
        // rechaza en vez de crear algo que quedaría colgado (los incidentes y tickets sí nacen pendientes).
        if (exigirCierre && tipo == TiposItem.Actividad && !puedeAsignar)
            return (400, "Las actividades se crean ya completadas, con su informe y su evidencia.", null);
        if (!puedeAsignar) prioridad = Prioridades.Media;
        if (!Prioridades.EsValida(prioridad)) return (400, ActividadRules.PrioridadError(prioridad), null);
        fechaLimite = FechaLimiteEfectiva(s, fechaLimite);
        if (fechaLimite is not null && fechaLimite.Value.Date < fecha.Date)
            return (400, "La fecha límite no puede ser anterior a la fecha de inicio.", null);

        usuario = (usuario ?? "").Trim();
        if (!puedeAsignar)
        {
            // Quien no puede asignar formalmente: sus tickets/incidentes quedan para revisión del
            // que sí puede; las actividades se las asigna a sí mismo.
            usuario = tipo == TiposItem.Ticket ? "" : s.Usuario;
            periodicidadDias = 0;
        }
        else if (usuario.Length == 0 && tipo != TiposItem.Ticket)
        {
            return (400, ActividadRules.UsuarioError(usuario), null);
        }
        if (usuario.Length > 0 && usuarios.Find(usuario) is not { Activo: true })
            return (400, "El usuario asignado no existe o está desactivado.", null);
        if (ActividadRules.PeriodicidadError(periodicidadDias) is string eP) return (400, eP, null);
        if (relacionId is not null && items.Find(relacionId.Value) is null)
            return (400, "El elemento relacionado no existe.", null);

        var borrador = new Actividad(0, tipo, Hospitales.Normalizar(hospital), titulo.Trim(), descripcion ?? "", usuario,
            fecha.Date, fechaLimite, EstadosActividad.Pendiente, prioridad, 0, "", "", s.Usuario, DateTime.Now, null,
            0, periodicidadDias, usuario.Length > 0 && puedeAsignar ? DateTime.Now : null, null, relacionId);

        var (status, error, item) = items.Create(borrador);
        if (status != 201 || item is null) return (status, error, null);

        historial.Registrar(item.Id, s.Usuario, "Alta", "", item.Etiqueta,
            $"{tipo} creada en {item.Hospital}{(item.Usuario.Length > 0 ? $" y asignada a {item.Usuario}" : " (sin asignar)")}");
        var evento = tipo switch
        {
            TiposItem.Ticket => AuditEvents.CrearTicket,
            TiposItem.Incidente => AuditEvents.CrearIncidente,
            _ => AuditEvents.CrearActividad,
        };
        var detalle = tipo == TiposItem.Ticket
            ? $"#{item.Numero:000} {item.Titulo} ({item.Hospital}){(item.Usuario.Length > 0 ? $" → {item.Usuario}" : "")}"
            : $"{item.Titulo} ({item.Hospital}){(item.Usuario.Length > 0 ? $" → {item.Usuario}" : "")} ({item.Fecha:dd/MM/yyyy})";
        bitacora.Register(s.Usuario, evento, detalle, ip);
        return (201, null, item);
    }

    /// <summary>El alta que **se cierra en el mismo momento**: «cuando el perfil de ingeniero asigna
    /// una actividad debe completarse en ese momento y pedir la evidencia». Un solo envío multipart
    /// hace las tres cosas —crear, adjuntar la evidencia y pasar a Completada—, y si algo falla después
    /// de crear se deshace el elemento para que no quede a medias. Todo se valida antes de escribir,
    /// así un archivo rechazado no deja una actividad pendiente ni media evidencia en disco.</summary>
    public (int Status, string? Error, Actividad? Item) CrearYCerrada(
        Sesion s, string tipo, string hospital, string titulo, string descripcion, DateTime? fecha,
        string informe, IReadOnlyList<(string Nombre, byte[]? Contenido)> archivos, string? ip)
    {
        if (fecha is null) return (400, "La fecha es obligatoria.", null);
        // El informe es opcional («Informe del trabajo realizado (opcional) + evidencia (obligatorio)»);
        // solo se vigila el tope de la columna.
        informe = (informe ?? "").Trim();
        if (informe.Length > 2000) return (400, "El informe no puede pasar de 2000 caracteres.", null);
        if (archivos.Count == 0)
            return (400, "Adjunta la evidencia: una foto o un archivo que respalde el trabajo.", null);
        if (archivos.Count > EvidenciaRules.MaxArchivosPorEnvio)
            return (400, $"Se admiten hasta {EvidenciaRules.MaxArchivosPorEnvio} archivos por envío.", null);
        foreach (var (nombre, contenido) in archivos)
            if (EvidenciaRules.ArchivoError(nombre, contenido) is string eA) return (400, eA, null);

        var (status, error, item) = Crear(s, tipo, hospital, titulo, descripcion, s.Usuario,
            fecha.Value, null, Prioridades.Media, 0, null, ip, exigirCierre: false);
        if (status != 201 || item is null) return (status, error, null);

        var (statusE, errorE, adjunta) = altaEvidencias.Adjuntar(s, item.Id, archivos, ip);
        if (statusE != 201)
        {
            DeshacerAlta(item);
            return (statusE, errorE, null);
        }

        var (statusC, errorC, cerrada) = CambiarEstado(s, item.Id, EstadosActividad.Completada, informe, ip);
        if (statusC != 200)
        {
            if (adjunta is not null) foreach (var e in adjunta) altaEvidencias.Descartar(e);
            DeshacerAlta(item);
            return (statusC, errorC, null);
        }
        return (201, null, cerrada);
    }

    /// La creación y su cierre viajan juntos: si el cierre no se pudo hacer, el elemento recién creado
    /// se quita del almacén en vez de dejarlo abierto a medias. Todavía no tiene reportes ni histórico
    /// que conservar; lo único que sobrevive es el renglón del alta fallida.
    private void DeshacerAlta(Actividad item) => items.Delete(item.Id);

    // ---- edición (Admin) ----

    public (int Status, string? Error, Actividad? Item) Editar(
        Sesion s, long id, string hospital, string titulo, string descripcion, string usuario, DateTime fecha,
        DateTime? fechaLimite, string prioridad, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.EditarItems)) return (403, "Solo el Administrador puede editar.", null);
        var actual = items.Find(id);
        if (actual is null) return (404, "La actividad no existe.", null);
        if (!EstadosActividad.Abierto(actual.Estado)) return (400, "Solo se pueden editar los elementos pendientes.", null);
        if (ActividadRules.HospitalError(hospital) is string eH) return (400, eH, null);
        if (catalogos.ErrorRegionDe(s.Usuario, hospital) is string eR) return (400, eR, null);
        if (ActividadRules.TituloError(titulo) is string eT) return (400, eT, null);
        if (!Prioridades.EsValida(prioridad)) return (400, ActividadRules.PrioridadError(prioridad), null);
        fechaLimite = FechaLimiteEfectiva(s, fechaLimite);
        if (fechaLimite is not null && fechaLimite.Value.Date < fecha.Date)
            return (400, "La fecha límite no puede ser anterior a la fecha de inicio.", null);
        usuario = (usuario ?? "").Trim();
        if (usuario.Length == 0 && actual.Tipo != TiposItem.Ticket) return (400, ActividadRules.UsuarioError(usuario), null);
        if (usuario.Length > 0 && usuarios.Find(usuario) is not { Activo: true })
            return (400, "El usuario asignado no existe o está desactivado.", null);

        var nueva = actual with
        {
            Hospital = Hospitales.Normalizar(hospital),
            Titulo = titulo.Trim(),
            Descripcion = descripcion ?? "",
            Usuario = usuario,
            Fecha = fecha.Date,
            FechaLimite = fechaLimite ?? (FechaLimiteHabilitada() ? null : actual.FechaLimite),
            Prioridad = prioridad,
        };
        var (status, error, guardada) = items.Guardar(nueva);
        if (status != 200 || guardada is null) return (status, error, null);

        RegistrarDiferencias(s, actual, guardada, ip);
        return (200, null, guardada);
    }

    private void RegistrarDiferencias(Sesion s, Actividad antes, Actividad despues, string? ip)
    {
        void Cambio(string campo, string a, string d)
        {
            if (a != d) historial.Registrar(despues.Id, s.Usuario, campo, a, d);
        }
        Cambio("Título", antes.Titulo, despues.Titulo);
        Cambio("Hospital", antes.Hospital, despues.Hospital);
        Cambio("Descripción", antes.Descripcion, despues.Descripcion);
        Cambio("Ingeniero", antes.Usuario.Length == 0 ? "(sin asignar)" : antes.Usuario, despues.Usuario.Length == 0 ? "(sin asignar)" : despues.Usuario);
        Cambio("Fecha de inicio", antes.Fecha.ToString("dd/MM/yyyy"), despues.Fecha.ToString("dd/MM/yyyy"));
        Cambio("Fecha límite", FmtFecha(antes.FechaLimite), FmtFecha(despues.FechaLimite));
        Cambio("Prioridad", antes.Prioridad, despues.Prioridad);
        if (antes.Usuario != despues.Usuario && despues.Usuario.Length > 0 && antes.Usuario.Length == 0)
        { /* primera asignación: la sella Asignar */ }
        bitacora.Register(s.Usuario, AuditEvents.EditarActividad, $"{despues.Etiqueta} ({despues.Tipo})", ip);
    }

    public static string FmtFecha(DateTime? f) => f is null ? "(sin fecha)" : f.Value.ToString("dd/MM/yyyy");

    // ---- eliminación (Admin, solo pendientes y sin historial de reportes) ----

    public (int Status, string? Error) Eliminar(Sesion s, long id, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.EliminarItems)) return (403, "Solo el Administrador puede eliminar.");
        var actual = items.Find(id);
        if (actual is null) return (404, "La actividad no existe.");
        if (actual.Estado != EstadosActividad.Pendiente)
            return (400, "Solo se pueden eliminar elementos pendientes; los demás se cancelan (no se borra el histórico).");
        if (reportes.Query(itemId: id).Count > 0)
            return (400, "El elemento ya tiene reportes de estatus: no se elimina (conservar históricos).");
        var (status, error) = items.Delete(id);
        if (status == 204)
            bitacora.Register(s.Usuario, AuditEvents.EliminarActividad, $"{actual.Etiqueta} ({actual.Tipo})", ip);
        return (status, error);
    }

    // ---- transiciones de estado ----

    public (int Status, string? Error, Actividad? Item) CambiarEstado(Sesion s, long id, string estado, string texto, string? ip)
    {
        if (!EstadosActividad.EsValido(estado)) return (400, "Estado no válido.", null);
        var actual = items.Find(id);
        if (actual is null) return (404, "La actividad no existe.", null);
        texto = (texto ?? "").Trim();

        switch (estado)
        {
            case EstadosActividad.Completada:
                if (!EsDuenoOAdmin(s, actual)) return (403, "No puedes completar un elemento que no tienes asignado.", null);
                if (!Tiene(s, CatalogoPermisos.ActualizarEstatus)) return (403, "Tu rol no puede actualizar estatus.", null);
                if (!EstadosActividad.Abierto(actual.Estado)) return (400, "El elemento ya está completado o cancelado: solo queda el registro.", null);
                // El informe es **opcional** desde el pedido «Informe del trabajo realizado
                // (opcional) + evidencia (obligatorio)»: lo que no puede faltar en el cierre es la
                // evidencia. Solo se vigila el tope de la columna.
                if (texto.Length > 2000) return (400, "El informe no puede pasar de 2000 caracteres.", null);
                // La evidencia (foto o archivo) es parte del cierre: se adjunta en el mismo envío
                // (`POST /api/actividades/{id}/completar`) y aquí se confirma que quedó al menos una,
                // para que nadie pueda marcar algo como completado por un camino que la salte.
                if (evidencias.PorItem(id).Count == 0)
                    return (400, "Falta la evidencia: adjunta una foto o un archivo del trabajo realizado.", null);
                break;

            case EstadosActividad.Cancelada:
                // Cancelar ya no es del responsable sino de quien administra: «el estado cancelado solo
                // admin o quien tenga ese permiso la puede cambiar a dicho estado». El permiso nuevo
                // `items.cancelar` es la única puerta (el Administrador lo trae por defecto).
                if (!Tiene(s, CatalogoPermisos.CancelarItems))
                    return (403, "Solo el Administrador o quien tenga el permiso «Cancelar actividades» puede cancelar.", null);
                if (!EstadosActividad.Abierto(actual.Estado))
                    return (400, "El elemento ya está completado o cancelado: solo queda el registro.", null);
                if (ActividadRules.TextoError(texto, "justificación", maximo: 500) is string eJ) return (400, eJ, null);
                break;

            default: // Pendiente
                return (400, "Un elemento no vuelve a Pendiente por esta vía; para eso está «Reabrir».", null);
        }

        var nueva = actual with
        {
            Estado = estado,
            Informe = estado == EstadosActividad.Completada ? Recortar(texto, 2000) : actual.Informe,
            Justificacion = estado == EstadosActividad.Cancelada ? Recortar(texto, 500) : actual.Justificacion,
            Avance = estado == EstadosActividad.Completada ? 100 : actual.Avance,
            Cerrado = estado is EstadosActividad.Completada or EstadosActividad.Cancelada
                ? actual.Cerrado ?? DateTime.Now
                : actual.Cerrado,
        };
        var (status, error, guardada) = items.Guardar(nueva);
        if (status != 200 || guardada is null) return (status, error, null);

        historial.Registrar(id, s.Usuario, "Estado", actual.Estado, estado, texto);
        var evento = estado switch
        {
            EstadosActividad.Completada => AuditEvents.CompletarActividad,
            EstadosActividad.Cancelada => AuditEvents.CancelarActividad,
            _ => AuditEvents.CambiarEstatus,
        };
        bitacora.Register(s.Usuario, evento, $"{guardada.Etiqueta} · {actual.Estado} → {estado}{(texto.Length > 0 ? $" · {Recortar(texto, 120)}" : "")}", ip);
        return (200, null, guardada);
    }

    // ---- reabrir (solo administrativo) ----

    /// <summary>Devolver una actividad **Completada** a **Pendiente**. Existe porque el alta del
    /// Ingeniero ya nace completada —«cuando el perfil de ingeniero asigna una actividad debe
    /// completarse en ese momento»—, así que el único camino de vuelta es la decisión del que tiene
    /// `items.reabrir`. La evidencia y el informe **se conservan**: lo que se reabre es el trabajo, no
    /// se borra lo que ya se enseñó de él. Después, quien reabre puede asignar y poner la fecha límite,
    /// que son decisiones que el Ingeniero nunca tuvo. Lo **Cancelado no se reabre**: «las canceladas
    /// ya no se pueden reabrir y solo queda el registro».</summary>
    public (int Status, string? Error, Actividad? Item) Reabrir(Sesion s, long id, string motivo, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.ReabrirItems))
            return (403, "Solo quien tenga el permiso «Reabrir actividades ya completadas» puede hacerlo.", null);
        var actual = items.Find(id);
        if (actual is null) return (404, "La actividad no existe.", null);
        if (actual.Estado == EstadosActividad.Cancelada)
            return (400, "Una actividad cancelada no se reabre: solo queda el registro.", null);
        if (actual.Estado != EstadosActividad.Completada)
            return (400, "Solo se reabren los elementos completados.", null);
        motivo = (motivo ?? "").Trim();
        if (motivo.Length > 1000) motivo = motivo[..1000];

        var nueva = actual with { Estado = EstadosActividad.Pendiente, Avance = 0, Cerrado = null };
        var (status, error, guardada) = items.Guardar(nueva);
        if (status != 200 || guardada is null) return (status, error, null);

        historial.Registrar(id, s.Usuario, "Estado", actual.Estado, EstadosActividad.Pendiente,
            motivo.Length > 0 ? motivo : "Reabierta sin comentario");
        bitacora.Register(s.Usuario, AuditEvents.ReabrirActividad,
            $"{guardada.Etiqueta} · {actual.Estado} → Pendiente{(motivo.Length > 0 ? $" · {Recortar(motivo, 120)}" : "")}", ip);
        return (200, null, guardada);
    }

    // ---- asignación formal (Admin) ----

    public (int Status, string? Error, Actividad? Item) Asignar(
        Sesion s, long id, string ingeniero, string prioridad, DateTime? fechaLimite, int periodicidadDias, string comentario, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.TicketsAsignar)) return (403, "Solo el Administrador puede asignar seguimiento.", null);
        var actual = items.Find(id);
        if (actual is null) return (404, "La actividad no existe.", null);
        if (!EstadosActividad.Abierto(actual.Estado)) return (400, "Un elemento completado o cancelado ya no se reasigna.", null);
        ingeniero = (ingeniero ?? "").Trim();
        if (ingeniero.Length == 0) return (400, ActividadRules.UsuarioError(ingeniero), null);
        if (usuarios.Find(ingeniero) is not { Activo: true }) return (400, "El ingeniero no existe o está desactivado.", null);
        if (!Prioridades.EsValida(prioridad)) return (400, ActividadRules.PrioridadError(prioridad), null);
        if (!Tiene(s, CatalogoPermisos.TicketsPrioridad) && prioridad != actual.Prioridad)
            return (403, "Tu rol no puede cambiar la prioridad.", null);
        // Con el interruptor apagado la fecha límite no se usa ni se valida: se ignora lo que llegue.
        if (FechaLimiteHabilitada())
        {
            if (fechaLimite is not null && fechaLimite.Value.Date < actual.Fecha.Date)
                return (400, "La fecha límite no puede ser anterior a la fecha de inicio.", null);
            if (!Tiene(s, CatalogoPermisos.TicketsFechaLimite) && fechaLimite?.Date != actual.FechaLimite?.Date)
                return (403, "Tu rol no puede cambiar la fecha límite.", null);
        }
        else fechaLimite = null;
        if (periodicidadDias <= 0) periodicidadDias = PeriodicidadPorDefecto();
        if (ActividadRules.PeriodicidadError(periodicidadDias) is string eP) return (400, eP, null);
        if (!Tiene(s, CatalogoPermisos.ReportesPeriodicidad) && periodicidadDias != actual.PeriodicidadDias)
            return (403, "Tu rol no puede cambiar la periodicidad de reportes.", null);

        var cambiaIngeniero = !string.Equals(actual.Usuario, ingeniero, StringComparison.OrdinalIgnoreCase);
        if (cambiaIngeniero && actual.Usuario.Length > 0 && !Tiene(s, CatalogoPermisos.TicketsCambiarIngeniero))
            return (403, "Tu rol no puede cambiar el ingeniero responsable.", null);

        var nueva = actual with
        {
            Usuario = ingeniero,
            Prioridad = prioridad,
            FechaLimite = fechaLimite?.Date ?? actual.FechaLimite,
            PeriodicidadDias = periodicidadDias,
            Asignado = cambiaIngeniero ? DateTime.Now : actual.Asignado,
        };
        var (status, error, guardada) = items.Guardar(nueva);
        if (status != 200 || guardada is null) return (status, error, null);

        if (cambiaIngeniero)
            historial.Registrar(id, s.Usuario, "Ingeniero", actual.Usuario.Length == 0 ? "(sin asignar)" : actual.Usuario, ingeniero, comentario);
        if (actual.Prioridad != prioridad) historial.Registrar(id, s.Usuario, "Prioridad", actual.Prioridad, prioridad);
        if (actual.FechaLimite?.Date != nueva.FechaLimite?.Date) historial.Registrar(id, s.Usuario, "Fecha límite", FmtFecha(actual.FechaLimite), FmtFecha(nueva.FechaLimite));
        if (actual.PeriodicidadDias != periodicidadDias) historial.Registrar(id, s.Usuario, "Periodicidad de reporte", $"{actual.PeriodicidadDias} días", $"{periodicidadDias} días");
        bitacora.Register(s.Usuario, AuditEvents.AsignarTicket,
            $"{guardada.Etiqueta} → {ingeniero} · prioridad {prioridad} · reporte cada {periodicidadDias} día(s)", ip);
        return (200, null, guardada);
    }

    // ---- observaciones y comentarios del responsable ----

    public (int Status, string? Error) Observacion(Sesion s, long id, string texto, string? ip)
    {
        var actual = items.Find(id);
        if (actual is null) return (404, "La actividad no existe.");
        // Una cancelada se congela: «las canceladas ya no se pueden reabrir y solo queda el registro».
        if (actual.Estado == EstadosActividad.Cancelada)
            return (400, "Una actividad cancelada ya no admite comentarios: solo queda el registro.");
        // El mismo camino sirve para dos cosas: la **observación** del que administra (permiso
        // `tickets.observaciones`) y el **comentario** del ingeniero responsable sobre su propio
        // elemento. Desde que se retiró el porcentaje de avance («el registrar avance queda en
        // comentarios»), el comentario es también la manera de informar cómo va el trabajo: cada uno
        // reinicia la cuenta del recordatorio de 3 días (`Actividad.LeTocaReporte`).
        var observa = Tiene(s, CatalogoPermisos.TicketsObservaciones);
        if (!observa && !EsDuenoOAdmin(s, actual)) return (403, "No puedes comentar un elemento que no tienes asignado.");
        if (ActividadRules.TextoError(texto, observa ? "observación" : "comentario", maximo: 1000) is string e) return (400, e);
        var campo = observa ? "Observación" : "Comentario";
        historial.Registrar(id, s.Usuario, campo, "", "", texto.Trim());
        bitacora.Register(s.Usuario, AuditEvents.Observacion, $"{actual.Etiqueta} · {campo} · {Recortar(texto.Trim(), 120)}", ip);
        return (204, null);
    }

    // ---- reportes de estatus ----

    public (int Status, string? Error, ReporteEstatus? Item) Reportar(
        Sesion s, long id, string estado, int avance, string descripcion, string problemas,
        string pendientes, string proximas, DateTime? fechaEstimada, string comentarios, string? ip)
    {
        var actual = items.Find(id);
        if (actual is null) return (404, "La actividad no existe.", null);
        if (!EsDuenoOAdmin(s, actual)) return (403, "No puedes reportar sobre un elemento que no tienes asignado.", null);
        if (!Tiene(s, CatalogoPermisos.ReportesEntregar)) return (403, "Tu rol no puede entregar reportes de estatus.", null);
        if (!EstadosActividad.Abierto(actual.Estado)) return (400, "El elemento ya está completado o cancelado; no admite reportes.", null);
        if (ReporteRules.DescripcionError(descripcion) is string eD) return (400, eD, null);
        if (ActividadRules.AvanceError(avance) is string eA) return (400, eA, null);
        // Con tres estados ya no hay "En proceso" al que mover el elemento: el reporte describe cómo
        // va, no cambia el estatus (lo que se escribe queda en el reporte y en el historial).
        estado = actual.Estado;

        var reporte = new ReporteEstatus(0, id, DateTime.Now, s.Usuario, estado, avance,
            Recortar(descripcion.Trim(), 2000), Recortar(problemas, 2000), Recortar(pendientes, 2000),
            Recortar(proximas, 2000), fechaEstimada?.Date, Recortar(comentarios, 2000));
        var (statusR, errorR, guardado) = reportes.Add(reporte);
        if (statusR != 201 || guardado is null) return (statusR, errorR, null);

        var nueva = actual with { UltimoReporte = guardado.Momento, Avance = avance };
        items.Guardar(nueva);
        historial.Registrar(id, s.Usuario, "Reporte de estatus", $"{actual.Avance}%", $"{avance}% · {estado}", Recortar(descripcion.Trim(), 400));
        bitacora.Register(s.Usuario, AuditEvents.EntregarReporte, $"{actual.Etiqueta} · {avance}% · {estado}", ip);
        return (201, null, guardado);
    }

    public IReadOnlyList<ReporteEstatus> ReportesDe(Sesion s, long id)
    {
        var actual = items.Find(id);
        if (actual is null || !EsDuenoOAdmin(s, actual)) return Array.Empty<ReporteEstatus>();
        return reportes.Query(itemId: id);
    }

    /// El historial de cambios **no** es parte del seguimiento ordinario del elemento: lo gobierna el
    /// permiso `tickets.historialCambios`. Sin ese permiso se devuelve vacío aunque se sea el dueño,
    /// para que la tarjeta ni siquiera ofrezca la pestaña.
    public IReadOnlyList<Cambio> HistorialDe(Sesion s, long id)
    {
        var actual = items.Find(id);
        if (actual is null || !EsDuenoOAdmin(s, actual)) return Array.Empty<Cambio>();
        if (!Tiene(s, CatalogoPermisos.TicketsHistorial)) return Array.Empty<Cambio>();
        return historial.PorItem(id);
    }

    private static string Recortar(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);
}
