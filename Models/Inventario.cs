namespace Meditechnosys.Intranet.Models;

/// Un campo del esquema de un inventario. Los tres inventarios (equipos, IPs y AETitle) se
/// describen con esta lista, de modo que la interfaz, la validación y la exportación a Excel se
/// generan a partir del mismo esquema: **ampliar un inventario es agregar un campo aquí**.
/// <param name="Clave">Nombre interno (se guarda así en la columna `datos`).</param>
/// <param name="Etiqueta">Texto que ve el usuario en la tabla, el formulario y el Excel.</param>
/// <param name="Tipo">`texto`, `area` (párrafo), `numero` o `lista` (desplegable).</param>
/// <param name="Opciones">Valores permitidos cuando `Tipo` es `lista`.</param>
/// <param name="Validacion">Regla extra opcional: `ip` (IPv4) o `puerto` (1–65535).</param>
public record CampoInventario(
    string Clave,
    string Etiqueta,
    string Tipo = "texto",
    bool Obligatorio = false,
    int Max = 120,
    IReadOnlyList<string>? Opciones = null,
    string Validacion = "");

/// Una colección del inventario (módulo independiente con su propia pantalla).
public record ColeccionInventario(string Clave, string Nombre, string Singular, IReadOnlyList<CampoInventario> Campos)
{
    public CampoInventario? Campo(string clave) => Campos.FirstOrDefault(c => c.Clave == clave);
}

/// Un registro de inventario. `Datos` guarda el valor de cada campo del esquema de su colección.
public record ItemInventario(
    long Id,
    string Coleccion,
    string Hospital,
    IReadOnlyDictionary<string, string> Datos,
    string CreadoPor,
    DateTime Creado,
    DateTime? Actualizado)
{
    public string Valor(string clave) => Datos.TryGetValue(clave, out var v) ? v : "";
}

/// Catálogo de los tres inventarios del módulo Gestión, clasificados por hospital.
public static class InventarioCatalogo
{
    public const string Equipos = "equipos";
    public const string Ips = "ips";
    public const string AeTitles = "aetitles";

    public static readonly ColeccionInventario ColeccionEquipos = new(Equipos, "Inventario de equipos", "Equipo", new[]
    {
        new CampoInventario("numInventario", "Número de inventario"),
        new CampoInventario("nombre", "Nombre del equipo", Obligatorio: true),
        new CampoInventario("categoria", "Categoría", "lista", Opciones: new[] { "Imagenología", "Laboratorio", "Cómputo", "Red", "Vida soporte", "Otro" }),
        new CampoInventario("marca", "Marca"),
        new CampoInventario("modelo", "Modelo"),
        new CampoInventario("serie", "Número de serie"),
        new CampoInventario("servicio", "Servicio / departamento"),
        new CampoInventario("ubicacion", "Ubicación / almacén"),
        new CampoInventario("cantidad", "Cantidad disponible", "numero"),
        new CampoInventario("estado", "Estado", "lista", Opciones: new[] { "Activo", "Disponible", "Asignado", "En mantenimiento", "Fuera de servicio", "En tránsito", "Baja" }),
        new CampoInventario("fechaIngreso", "Fecha de ingreso al inventario"),
        new CampoInventario("procedencia", "Procedencia"),
        new CampoInventario("docEntrada", "Documento de entrada"),
        new CampoInventario("notas", "Notas", "area", Max: 500),
    });

    public static readonly ColeccionInventario ColeccionIps = new(Ips, "Inventario de IPs", "Dirección IP", new[]
    {
        new CampoInventario("descripcion", "Equipo o servicio", Obligatorio: true),
        new CampoInventario("ip", "Dirección IP", Obligatorio: true, Validacion: "ip"),
        new CampoInventario("mascara", "Máscara de subred", Validacion: "ip"),
        new CampoInventario("puerta", "Puerta de enlace", Validacion: "ip"),
        new CampoInventario("asignacion", "Asignación", "lista", Opciones: new[] { "Fija", "Dinámica", "Reservada" }),
        new CampoInventario("vlan", "VLAN", Max: 20),
        new CampoInventario("notas", "Notas", "area", Max: 500),
    });

    public static readonly ColeccionInventario ColeccionAeTitles = new(AeTitles, "Inventario de AETitle", "AE Title", new[]
    {
        new CampoInventario("descripcion", "Descripción", Obligatorio: true),
        new CampoInventario("aetitle", "AE Title", Obligatorio: true, Max: 40),
        new CampoInventario("host", "Host / IP", Obligatorio: true),
        new CampoInventario("puerto", "Puerto", "numero", Validacion: "puerto"),
        new CampoInventario("modalidad", "Modalidad", "lista", Opciones: new[] { "CR", "CT", "DX", "MG", "MR", "PT", "RF", "US", "WSD", "Otro" }),
        new CampoInventario("rol", "Rol DICOM", "lista", Opciones: new[] { "Nodo PACS", "Modalidad", "Workstation", "Receptor", "Otro" }),
        new CampoInventario("notas", "Notas", "area", Max: 500),
    });

    public static readonly IReadOnlyList<ColeccionInventario> Colecciones = new[] { ColeccionEquipos, ColeccionIps, ColeccionAeTitles };

    public static ColeccionInventario? PorClave(string? clave) =>
        Colecciones.FirstOrDefault(c => string.Equals(c.Clave, clave, StringComparison.OrdinalIgnoreCase));

    /// Nombre de archivo base para la exportación a Excel (`Inventario_Equipos_HES`).
    public static string NombreArchivo(string clave, string? hospital)
    {
        var c = PorClave(clave);
        var nombre = c is null ? "Inventario" : c.Nombre.Replace("Inventario de ", "Inventario_").Replace(' ', '_');
        var h = Hospitales.Normalizar(hospital);
        return h.Length > 0 ? $"{nombre}_{h}" : nombre;
    }
}

/// Validaciones compartidas por el almacén, el servicio y la API de inventarios.
public static class InventarioRules
{
    // Se permite "General" para representar el almacén central (unidad "Almacén") en equipos.
    public static string? HospitalError(string? hospital) => Hospitales.Error(hospital, permiteGeneral: true);

    public static string? ErrorCampo(CampoInventario campo, string valor)
    {
        valor = (valor ?? "").Trim();
        if (valor.Length == 0)
            return campo.Obligatorio ? $"{campo.Etiqueta} es obligatorio." : null;
        if (valor.Length > campo.Max)
            return $"{campo.Etiqueta} no puede pasar de {campo.Max} caracteres.";
        if (campo.Tipo == "lista" && campo.Opciones is { Count: > 0 } && !campo.Opciones.Contains(valor, StringComparer.OrdinalIgnoreCase))
            return $"{campo.Etiqueta} debe ser uno de: {string.Join(", ", campo.Opciones)}.";
        if (campo.Tipo == "numero" && !int.TryParse(valor, out _))
            return $"{campo.Etiqueta} debe ser un número.";
        return campo.Validacion switch
        {
            "ip" when !IpValida(valor) => $"{campo.Etiqueta} no es una dirección IPv4 válida.",
            "puerto" when !PuertoValido(valor) => $"{campo.Etiqueta} debe ser un puerto entre 1 y 65535.",
            _ => null,
        };
    }

    /// Valida hospital + campos del esquema. Devuelve el primer error o null.
    public static string? Validar(ColeccionInventario coleccion, string? hospital, IReadOnlyDictionary<string, string> datos)
    {
        if (HospitalError(hospital) is string eH) return eH;
        foreach (var campo in coleccion.Campos)
        {
            if (ErrorCampo(campo, datos.TryGetValue(campo.Clave, out var v) ? v : "") is string e) return e;
        }
        return null;
    }

    public static bool IpValida(string valor)
    {
        var partes = valor.Split('.');
        if (partes.Length != 4) return false;
        foreach (var p in partes)
        {
            if (p.Length is 0 or > 3 || !int.TryParse(p, out var n) || n is < 0 or > 255) return false;
            if (p.Length > 1 && p[0] == '0') return false;
        }
        return true;
    }

    public static bool PuertoValido(string valor) => int.TryParse(valor, out var p) && p is >= 1 and <= 65535;
}
