namespace Meditechnosys.Intranet.Models;

/// Clasificación por hospital. Toda la información del módulo Gestión (actividades, incidentes,
/// tickets, los tres inventarios y el Playbook) se guarda etiquetada con uno de estos valores,
/// de modo que se pueda consultar y exportar por hospital.
public static class Hospitales
{
    public const string HES = "HES";
    public const string HGR = "HGR";
    public const string HGO = "HGO";
    public const string HI = "HI";

    /// Hospitales del IMSS que maneja el sistema.
    public static readonly IReadOnlyList<string> Todos = new[] { HES, HGR, HGO, HI };

    /// Etiqueta adicional para documentación que no corresponde a un hospital concreto.
    public const string General = "General";

    // Los códigos que agregue la pantalla Programador. Se registran al arrancar el servicio y cada
    // vez que se guarda el catálogo (ver GestionCatalogos.AplicarHospitales). Los cuatro de siempre
    // siguen siendo válidos estén o no en la lista configurada: si no, cualquier consulta o edición
    // de un registro histórico quedaría invalidada por borrar un hospital del catálogo.
    private static volatile string[] _extras = Array.Empty<string>();

    /// Códigos agregados por el Programador (fuera de los cuatro de siempre).
    public static IReadOnlyList<string> Extras => _extras;

    /// Todos los hospitales válidos: los fijos más los configurados.
    public static IReadOnlyList<string> Efectivos =>
        _extras.Length == 0 ? Todos : Todos.Concat(_extras).ToList();

    /// Hospitales + General (lo usa el Playbook, donde la clasificación puede no aplicar).
    public static IReadOnlyList<string> ConGeneral => Efectivos.Append(General).ToList();

    /// Reemplaza los códigos configurados. Se llama desde GestionCatalogos; nunca desde datos del
    /// usuario sin validar (ver GestionCatalogos.GuardarLista).
    public static void ActualizarExtras(IEnumerable<string>? extras) =>
        _extras = (extras ?? Array.Empty<string>())
            .Select(e => (e ?? "").Trim())
            .Where(e => e.Length > 0 && !Todos.Contains(e, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool EsValido(string? hospital, bool permiteGeneral = false) =>
        hospital is not null && (permiteGeneral ? ConGeneral : Efectivos).Contains(hospital, StringComparer.Ordinal);

    /// Normaliza mayúsculas/espacios: "hes " → "HES". Devuelve "" si no es un hospital conocido.
    public static string Normalizar(string? hospital, bool permiteGeneral = false)
    {
        var h = (hospital ?? "").Trim();
        if (h.Length == 0) return "";
        var candidatos = permiteGeneral ? ConGeneral : Efectivos;
        return candidatos.FirstOrDefault(c => string.Equals(c, h, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    public static string? Error(string? hospital, bool permiteGeneral = false) =>
        string.IsNullOrWhiteSpace(hospital)
            ? "El hospital es obligatorio."
            : EsValido(hospital.Trim(), permiteGeneral)
                ? null
                : $"El hospital debe ser uno de: {string.Join(", ", permiteGeneral ? ConGeneral : Efectivos)}.";
}
