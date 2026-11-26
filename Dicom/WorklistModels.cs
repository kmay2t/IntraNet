using System.ComponentModel.DataAnnotations;

namespace Meditechnosys.Intranet.Dicom;

// Parámetros por petición: nunca se modifica LocalOptions ni un nodo compartido.
public sealed class WorklistConnection : IValidatableObject
{
    [Required(ErrorMessage = "Escribe el host o la dirección IP del nodo."),
        StringLength(253, ErrorMessage = "El host o la dirección IP admiten hasta 253 caracteres.")]
    public string Host { get; set; } = "";

    [Range(1, 65535, ErrorMessage = "El puerto debe ser un número entre 1 y 65535.")]
    public int Puerto { get; set; } = 104;

    [Required(ErrorMessage = "Escribe el AE Title remoto del nodo."),
        StringLength(16, ErrorMessage = "El AE Title admite hasta 16 caracteres ASCII, sin barra inversa."),
        RegularExpression(@"^[\x20-\x5B\x5D-\x7E]+$",
        ErrorMessage = "El AE Title admite hasta 16 caracteres ASCII, sin barra inversa.")]
    public string AeRemoto { get; set; } = "";

    [Required(ErrorMessage = "Escribe el AE Title local."),
        StringLength(16, ErrorMessage = "El AE Title admite hasta 16 caracteres ASCII, sin barra inversa."),
        RegularExpression(@"^[\x20-\x5B\x5D-\x7E]+$",
        ErrorMessage = "El AE Title admite hasta 16 caracteres ASCII, sin barra inversa.")]
    public string AeLocal { get; set; } = "MEDI";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var host = Host?.Trim() ?? "";
        if (host.Length > 0 && !System.Net.IPAddress.TryParse(host, out _)
            && Uri.CheckHostName(host) != UriHostNameType.Dns)
            yield return new ValidationResult("Escribe una IP o nombre de servidor, sin URL ni ruta.", new[] { nameof(Host) });
    }
}

public sealed class WorklistSearchRequest : IValidatableObject
{
    [Required(ErrorMessage = "Faltan los datos de conexión del nodo.")]
    public WorklistConnection Conexion { get; set; } = new();

    [RegularExpression(@"^[A-Z0-9_ ]{0,16}$",
        ErrorMessage = "La modalidad debe ser un código de hasta 16 caracteres en mayúsculas.")]
    public string? Modalidad { get; set; }

    // Fecha DICOM (VR DA): AAAAMMDD. Vacío = cualquier fecha programada.
    [RegularExpression(@"^\d{8}$", ErrorMessage = "La fecha de inicio debe tener 8 dígitos: AAAAMMDD.")]
    public string? FechaDesde { get; set; }

    [RegularExpression(@"^\d{8}$", ErrorMessage = "La fecha de fin debe tener 8 dígitos: AAAAMMDD.")]
    public string? FechaHasta { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (value, nombre) in new[] { (FechaDesde, nameof(FechaDesde)), (FechaHasta, nameof(FechaHasta)) })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{8}$")
                || !DateTime.TryParseExact(value, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _))
                yield return new ValidationResult($"La {NombreFecha(nombre)} no es una fecha válida.", new[] { nombre });
        }
        if (!string.IsNullOrWhiteSpace(FechaDesde) && !string.IsNullOrWhiteSpace(FechaHasta)
            && string.CompareOrdinal(FechaDesde, FechaHasta) > 0)
            yield return new ValidationResult("La fecha de inicio no puede ser posterior a la fecha de fin.",
                new[] { nameof(FechaDesde), nameof(FechaHasta) });
    }

    private static string NombreFecha(string nombre) => nombre == nameof(FechaDesde) ? "inicio" : "fin";

    public string? Desde => string.IsNullOrWhiteSpace(FechaDesde) ? null : FechaDesde.Trim();
    public string? Hasta => string.IsNullOrWhiteSpace(FechaHasta) ? null : FechaHasta.Trim();
}

// Datos que identifican la imagen de prueba. Viajan solo en el cuerpo del POST: nunca se ponen en la URL
// ni se guardan en el navegador. Del envío queda registro en el servidor (App_Data/dicom-pruebas.jsonl)
// con los tags que salieron, porque es lo que se pidió poder revisar después.
public sealed class WorklistPruebaDatos : IValidatableObject
{
    public const string Anonima = "anonima";
    public const string Seleccion = "seleccion";
    public const string Manual = "manual";

    [Required(ErrorMessage = "Elige si la prueba es anónima, de una entrada seleccionada o con datos escritos a mano."),
        RegularExpression(@"^(anonima|seleccion|manual)$",
        ErrorMessage = "Elige si la prueba es anónima, de una entrada seleccionada o con datos escritos a mano.")]
    public string Origen { get; set; } = Anonima;

    // VR PN: separador de componentes "^"; se prohíbe la barra inversa y los caracteres de control.
    [StringLength(200, ErrorMessage = "El nombre del paciente admite hasta 200 caracteres.")]
    public string? Paciente { get; set; }

    // VR LO: hasta 64 caracteres.
    [StringLength(64, ErrorMessage = "El ID del paciente admite hasta 64 caracteres.")]
    public string? IdPaciente { get; set; }

    // VR SH: alphanumericos y . _ -, hasta 16.
    [RegularExpression(@"^[A-Za-z0-9 ._-]{0,16}$",
        ErrorMessage = "El número de acceso admite hasta 16 caracteres: letras, números, espacio, punto, guion o guion bajo.")]
    public string? Acceso { get; set; }

    // VR UI: dígitos separados por puntos, hasta 64. Es el StudyInstanceUID **programado** de la entrada
    // seleccionada. Se acepta solo con origen `seleccion`: la prueba se manda dentro de ese estudio, que
    // es lo que hace un equipo real y lo que evita que el destino abra un segundo registro bajo el mismo
    // acceso. Con origen anónima o escrita a mano se ignora y el estudio es nuevo.
    [RegularExpression(@"^[0-9.]{1,64}$",
        ErrorMessage = "El UID de estudio de la entrada no tiene formato DICOM: solo dígitos y puntos, máximo 64 caracteres.")]
    [StringLength(64, ErrorMessage = "El UID de estudio de la entrada admite hasta 64 caracteres.")]
    public string? EstudioUid { get; set; }

    public bool EsAnonima => string.Equals(Origen, Anonima, StringComparison.OrdinalIgnoreCase);

    public bool EsSeleccion => string.Equals(Origen, Seleccion, StringComparison.OrdinalIgnoreCase);

    // ---------- lo que la entrada trajo y hoy no viajaba ----------
    // Se mandan **tal cual** llegaron del nodo. Solo tienen efecto con origen `seleccion` y con su casilla
    // marcada; en anónima y manual el origen no los provide.

    // VR CS: el código de modalidad de la entrada (MR, CT…).
    [RegularExpression(@"^[A-Z0-9_ ]{0,16}$",
        ErrorMessage = "La modalidad de la entrada debe ser un código de hasta 16 caracteres en mayúsculas.")]
    public string? Modalidad { get; set; }

    // VR LO: descripción del procedimiento programado.
    [StringLength(200, ErrorMessage = "El procedimiento de la entrada admite hasta 200 caracteres.")]
    public string? Procedimiento { get; set; }

    // VR DA de la cita programada (AAAAMMDD).
    [RegularExpression(@"^\d{8}$", ErrorMessage = "La fecha programada de la entrada debe tener 8 dígitos: AAAAMMDD.")]
    public string? FechaProgramada { get; set; }

    // VR TM de la cita programada; el nodo puede mandar precisión reducida (HH, HHMM o HHMMSS).
    [RegularExpression(@"^\d{2}(\d{2}(\d{2}(\.\d{1,6})?)?)?$",
        ErrorMessage = "La hora programada de la entrada no tiene formato DICOM: HH, HHMM o HHMMSS.")]
    public string? HoraProgramada { get; set; }

    // ---------- el menú de datos que regresan ----------
    // Nombres de las casillas marcadas en la pantalla. `null` (un cliente antiguo o un origen que no tiene
    // menú) significa «todos», que es como se comportaba la prueba antes de este cambio.
    public List<string>? Campos { get; set; }

    public static readonly IReadOnlyList<(string Clave, string Etiqueta)> Regresables =
        new[]
        {
            ("nombre", "Nombre"), ("id", "ID del paciente"), ("acceso", "Número de acceso"),
            ("modalidad", "Modalidad"), ("procedimiento", "Procedimiento"),
            ("programado", "Fecha y hora programadas"), ("estudioUid", "UID del estudio")
        };

    public bool Incluye(string clave) => Campos is null
        || Campos.Any(c => string.Equals(c?.Trim(), clave, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EsAnonima) yield break;
        var paciente = Paciente?.Trim() ?? "";
        var id = IdPaciente?.Trim() ?? "";
        // La exigencia de nombre e ID solo aplica a lo que el operador marcó: si desmarcó «Nombre»,
        // no hay nada que exigir y la imagen sale con la identidad sintética.
        if (Incluye("nombre") && paciente.Length == 0)
            yield return new ValidationResult("Escribe el nombre del paciente para la prueba.", new[] { nameof(Paciente) });
        else if (!EsTextoValido(paciente))
            yield return new ValidationResult("El nombre no admite barra inversa ni caracteres de control.", new[] { nameof(Paciente) });
        if (Incluye("id") && id.Length == 0)
            yield return new ValidationResult("Escribe el ID del paciente para la prueba.", new[] { nameof(IdPaciente) });
        else if (!EsTextoValido(id))
            yield return new ValidationResult("El ID del paciente no admite barra inversa ni caracteres de control.", new[] { nameof(IdPaciente) });
        if (!EsTextoValido(Procedimiento ?? ""))
            yield return new ValidationResult("El procedimiento no admite barra inversa ni caracteres de control.", new[] { nameof(Procedimiento) });
    }

    private static bool EsTextoValido(string valor) =>
        !valor.Contains('\\') && valor.All(c => !char.IsControl(c));
}

public sealed class WorklistTestRequest
{
    [Required(ErrorMessage = "Faltan los datos de conexión del destino.")]
    public WorklistConnection Conexion { get; set; } = new();

    [Required(ErrorMessage = "Faltan los datos de la prueba: elige el origen de los datos.")]
    public WorklistPruebaDatos Datos { get; set; } = new();

    public bool Confirmar { get; set; }
}

public sealed record WorklistItem(string Paciente, string IdPaciente, string Acceso,
    string Procedimiento, string Modalidad, string Fecha, string Hora, string Estacion, string Estado,
    // El UID programado se muestra en pantalla y, con origen `seleccion`, la prueba se manda dentro de él.
    string EstudioUid);

public sealed record WorklistSearchResult(IReadOnlyList<WorklistItem> Estudios,
    int OmitidosSinEstado, int OmitidosNoProgramados, bool Limitado)
{
    public int Total => Estudios.Count;
}

// Un dato de la prueba, tal y como quedó escrito en la imagen. La pantalla lo muestra en su ventana de
// logs para que el operador vea **qué viajó y qué no**, y por qué. `Valor` es el contenido final del tag
// (el valor técnico por defecto cuando el dato no viaja), así que la respuesta no inventa nada.
public sealed record PruebaCampo(string Dato, string Etiqueta, string Valor, bool Viajo, string Motivo);

// `Campos` devuelve al navegador los datos que realmente fueron a la imagen; `Tags` son los identificadores
// que quedaron escritos en ella y `Verificacion` es lo que el propio destino respondió cuando se le preguntó
// por ese estudio. Nada de esto se queda solo en la pantalla: el controlador lo guarda en el registro
// permanente de la intranet (App_Data/dicom-pruebas.jsonl) y la pantalla lo lee de ahí.
public sealed record WorklistTestResult(string Estado, string Mensaje, IReadOnlyList<PruebaCampo> Campos,
    PruebaTags Tags, PruebaVerificacion Verificacion);

public interface IWorklistService
{
    Task<WorklistSearchResult> SearchAsync(WorklistSearchRequest request, CancellationToken ct);
    Task<bool> EchoAsync(WorklistConnection connection, CancellationToken ct);
    Task<WorklistTestResult> SendTestAsync(WorklistTestRequest request, CancellationToken ct);
}

public sealed class WorklistException(string message, bool timeout = false) : Exception(message)
{
    public bool Timeout { get; } = timeout;
}
