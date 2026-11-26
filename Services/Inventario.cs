using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

/// Reglas de negocio de los tres inventarios (equipos, IPs y AETitle). Cada inventario es un módulo
/// independiente con su propia pantalla; comparten almacén, permisos, filtros por hospital y la
/// exportación a Excel. Consulta y descarga están disponibles para Admin e Ingeniero; dar de alta,
/// editar y eliminar es exclusivo del Administrador.
public class InventarioService(IInventarioStore store, IAuditLog bitacora, GestionCatalogos catalogos)
{
    public bool Tiene(GestionService.Sesion s, string clave) => s.Puede(clave);

    public ColeccionInventario? Coleccion(string? clave) => InventarioCatalogo.PorClave(clave);

    /// Lista filtrada por hospital ("" = todos) y texto libre sobre cualquier campo del esquema. Antes
    /// de todo eso se quita lo que no sea de la región del usuario: el filtro de la pantalla no puede
    /// abrir una región que su cuenta no ve.
    public IReadOnlyList<ItemInventario> Filtrar(GestionService.Sesion s, string coleccion, string? hospital, string? texto)
    {
        if (!Tiene(s, CatalogoPermisos.InventarioConsultar)) return Array.Empty<ItemInventario>();
        var h = Hospitales.Normalizar(hospital, permiteGeneral: true);
        var t = (texto ?? "").Trim();
        return catalogos.FiltraPorRegion(s.Usuario, store.List(coleccion), i => i.Hospital)
            .Where(i => h.Length == 0 || string.Equals(i.Hospital, h, StringComparison.Ordinal))
            .Where(i => t.Length == 0 || Coincide(i, t))
            .ToList();
    }

    private static bool Coincide(ItemInventario i, string texto)
    {
        // Sin acentos ni mayúsculas: "muno" encuentra "MUÑOZ" y "cranio" encuentra "CRÁNEO".
        var t = Texto.Normalizar(texto);
        if (t.Length == 0) return true;
        if (Texto.Normalizar(i.Hospital).Contains(t, StringComparison.Ordinal)) return true;
        if (Texto.Normalizar(i.CreadoPor).Contains(t, StringComparison.Ordinal)) return true;
        return i.Datos.Values.Any(v => Texto.Normalizar(v).Contains(t, StringComparison.Ordinal));
    }

    public (int Status, string? Error, ItemInventario? Item) Crear(
        GestionService.Sesion s, string coleccion, string hospital, IReadOnlyDictionary<string, string> datos, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.InventarioAdministrar))
            return (403, "Solo el Administrador puede dar de alta registros en el inventario.", null);
        if (InventarioCatalogo.PorClave(coleccion) is not { } c) return (400, "El inventario no existe.", null);
        if (InventarioRules.Validar(c, hospital, datos) is string e) return (400, e, null);
        if (catalogos.ErrorRegionDe(s.Usuario, hospital) is string eR) return (400, eR, null);

        // En equipos el número de inventario se genera solo (EQ-0001, EQ-0002, …) si no se indicó.
        var datosGuardar = datos;
        if (c.Clave == InventarioCatalogo.Equipos
            && !(datos.TryGetValue("numInventario", out var nin) && !string.IsNullOrWhiteSpace(nin)))
        {
            datosGuardar = new Dictionary<string, string>(datos, StringComparer.Ordinal)
            {
                ["numInventario"] = SiguienteNumInventario(),
            };
        }

        var item = new ItemInventario(0, c.Clave, Hospitales.Normalizar(hospital, permiteGeneral: true), datosGuardar, s.Usuario, DateTime.Now, null);
        var (status, error, guardado) = store.Create(item);
        if (status != 201 || guardado is null) return (status, error, null);
        bitacora.Register(s.Usuario, AuditEvents.CrearItemInventario, $"{c.Nombre} · {guardado.Hospital} · {Resumen(c, guardado)}", ip);
        return (201, null, guardado);
    }

    /// Siguiente número de inventario automático para equipos: `EQ-0001`, `EQ-0002`, …
    /// Toma el máximo ya usado con ese patrón sobre **todos** los equipos (incluidos los que están
    /// en cantidad 0 y ya no se listan) para no reutilizar nunca un número dado de baja.
    private string SiguienteNumInventario()
    {
        var max = 0;
        foreach (var i in store.List(InventarioCatalogo.Equipos))
        {
            var n = i.Valor("numInventario").Trim();
            if (n.Length > 3
                && n.StartsWith("EQ-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(n.AsSpan(3), out var v)
                && v > max)
                max = v;
        }
        return $"EQ-{max + 1:D4}";
    }

    public (int Status, string? Error, ItemInventario? Item) Editar(
        GestionService.Sesion s, string coleccion, long id, string hospital, IReadOnlyDictionary<string, string> datos, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.InventarioAdministrar))
            return (403, "Solo el Administrador puede editar el inventario.", null);
        if (InventarioCatalogo.PorClave(coleccion) is not { } c) return (400, "El inventario no existe.", null);
        var actual = store.Find(c.Clave, id);
        if (actual is null) return (404, "El registro no existe.", null);
        if (InventarioRules.Validar(c, hospital, datos) is string e) return (400, e, null);
        // Se chequea el registro guardado y el que se quiere escribir: cambiar una ficha de la región
        // de uno a otra, o sacarla de ella, tampoco le toca a quien tiene una región asignada.
        if (catalogos.ErrorRegionDe(s.Usuario, actual.Hospital) is string eActual) return (400, eActual, null);
        if (catalogos.ErrorRegionDe(s.Usuario, hospital) is string eR) return (400, eR, null);

        var (status, error, guardado) = store.Guardar(actual with
        {
            Hospital = Hospitales.Normalizar(hospital, permiteGeneral: true),
            Datos = datos,
            Actualizado = DateTime.Now,
        });
        if (status != 200 || guardado is null) return (status, error, null);
        bitacora.Register(s.Usuario, AuditEvents.EditarItemInventario, $"{c.Nombre} · {guardado.Hospital} · {Resumen(c, guardado)}", ip);
        return (200, null, guardado);
    }

    public (int Status, string? Error) Eliminar(GestionService.Sesion s, string coleccion, long id, string? ip)
    {
        if (!Tiene(s, CatalogoPermisos.InventarioAdministrar))
            return (403, "Solo el Administrador puede eliminar registros del inventario.");
        if (InventarioCatalogo.PorClave(coleccion) is not { } c) return (400, "El inventario no existe.");
        var actual = store.Find(c.Clave, id);
        if (actual is null) return (404, "El registro no existe.");
        if (catalogos.ErrorRegionDe(s.Usuario, actual.Hospital) is string eR) return (400, eR);
        var (status, error) = store.Delete(c.Clave, id);
        if (status == 204)
            bitacora.Register(s.Usuario, AuditEvents.EliminarItemInventario, $"{c.Nombre} · {actual.Hospital} · {Resumen(c, actual)}", ip);
        return (status, error);
    }

    /// Descarga .xlsx del inventario respetando el filtro de hospital y la búsqueda aplicados.
    public (int Status, string? Error, string? Nombre, byte[]? Archivo) Exportar(
        GestionService.Sesion s, string coleccion, string? hospital, string? texto)
    {
        if (!Tiene(s, CatalogoPermisos.InventarioExportar))
            return (403, "Tu rol no puede descargar el inventario en Excel.", null, null);
        if (InventarioCatalogo.PorClave(coleccion) is not { } c) return (400, "El inventario no existe.", null, null);

        var lista = Filtrar(s, coleccion, hospital, texto);
        var encabezados = new List<string> { "ID", "Hospital" };
        encabezados.AddRange(c.Campos.Select(f => f.Etiqueta));
        encabezados.AddRange(new[] { "Registrado por", "Fecha de alta", "Última actualización" });

        var filas = lista.Select(i =>
        {
            var fila = new List<string> { i.Id.ToString(), i.Hospital };
            fila.AddRange(c.Campos.Select(f => i.Valor(f.Clave)));
            fila.Add(i.CreadoPor);
            fila.Add(Excel.FmtFechaHora(i.Creado));
            fila.Add(Excel.FmtFechaHora(i.Actualizado));
            return (IReadOnlyList<string>)fila;
        }).ToList();

        var nombre = InventarioCatalogo.NombreArchivo(c.Clave, hospital) + ".xlsx";
        var hoja = c.Nombre.Length > 31 ? c.Clave : c.Nombre;
        return (200, null, nombre, Excel.Tabla(hoja, encabezados, filas));
    }

    /// Texto corto que identifica al registro en la bitácora (primer campo obligatorio o el primero).
    private static string Resumen(ColeccionInventario c, ItemInventario i)
    {
        var clave = c.Campos.FirstOrDefault(f => f.Obligatorio)?.Clave ?? c.Campos.FirstOrDefault()?.Clave;
        return clave is null ? $"id={i.Id}" : i.Valor(clave);
    }
}
