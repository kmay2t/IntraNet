namespace Meditechnosys.Intranet.Models;

/// Estados de un documento del Playbook.
public static class EstadosDocumento
{
    public const string Vigente = "Vigente";
    public const string Retirado = "Retirado";

    public static readonly IReadOnlyList<string> Todos = new[] { Vigente, Retirado };

    public static bool EsValido(string? estado) => estado is not null && Todos.Contains(estado, StringComparer.Ordinal);
}

/// Tipos de documento del Playbook: clasifica el PDF según su naturaleza técnica.
public static class TiposDocumento
{
    public const string Manual = "Manual";
    public const string Procedimiento = "Procedimiento";
    public const string Guia = "Guía";
    public const string Instructivo = "Instructivo";
    public const string Otro = "Otro";

    public static readonly IReadOnlyList<string> Todos = new[] { Manual, Procedimiento, Guia, Instructivo, Otro };

    public static bool EsValido(string? tipo) => tipo is not null && Todos.Contains(tipo, StringComparer.Ordinal);
}

/// Un documento técnico del Playbook (PDF) clasificado por hospital.
/// El contenido del archivo vive en disco (`Playbook:StorageDir`); aquí van sus datos y la ruta.
/// <param name="Nombre">Título con el que se consulta (p. ej. "Procedimiento de configuración DICOM").</param>
/// <param name="NombreArchivo">Nombre del archivo PDF original que subió el Administrador.</param>
/// <param name="Version">Versión declarada (p. ej. "v2.1"); cambiarla sella `FechaActualizacion`.</param>
/// <param name="Hospital">Hospital al que corresponde, o `General` si no aplica a uno concreto.</param>
/// <param name="FechaPublicacion">Fecha que el Administrador declara como publicación.</param>
/// <param name="FechaActualizacion">Se fija sola al cambiar la versión o reemplazar el PDF.</param>
/// <param name="Ruta">Ruta relativa dentro de la carpeta de almacenamiento.</param>
/// <param name="Tipo">Naturaleza del documento (Manual, Procedimiento, Guía, Instructivo u Otro).</param>
public record Documento(
    long Id,
    string Nombre,
    string NombreArchivo,
    string Version,
    string Hospital,
    DateTime? FechaPublicacion,
    DateTime? FechaActualizacion,
    string SubidoPor,
    DateTime Subido,
    long Tamano,
    string Ruta,
    string Estado,
    string Tipo = TiposDocumento.Otro)
{
    public static string Formato => "PDF";
}

public static class DocumentoRules
{
    public const long MaxPdfBytes = 20 * 1024 * 1024;

    public static string? NombreError(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? "El nombre del documento es obligatorio."
        : nombre.Trim().Length < 3 ? "El nombre debe tener al menos 3 caracteres."
        : nombre.Trim().Length > 150 ? "El nombre no puede pasar de 150 caracteres."
        : null;

    public static string? VersionError(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "La versión es obligatoria (p. ej. v1.0)."
        : version.Trim().Length > 20 ? "La versión no puede pasar de 20 caracteres."
        : null;

    public static string? EstadoError(string? estado) =>
        EstadosDocumento.EsValido(estado) ? null : "El estado debe ser Vigente o Retirado.";

    public static string? TipoError(string? tipo) =>
        TiposDocumento.EsValido(tipo) ? null : "Selecciona un tipo de documento válido (Manual, Procedimiento, Guía, Instructivo u Otro).";

    /// Valida el archivo subido: tamaño y firma de PDF real (no basta la extensión).
    public static string? ArchivoError(byte[]? contenido) =>
        contenido is null || contenido.Length == 0 ? "Selecciona un archivo PDF."
        : contenido.Length > MaxPdfBytes ? "El archivo excede 20 MB."
        : !contenido.AsSpan(0, 5).SequenceEqual("%PDF-"u8) ? "El archivo no es un PDF válido."
        : null;
}
