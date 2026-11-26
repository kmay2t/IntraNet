namespace Meditechnosys.Intranet.Models;

/// <summary>
/// Evidencia del trabajo reportado al completar una actividad, incidente o ticket: la **foto o el
/// archivo** que entrega quien cierra el elemento. Sin evidencia el elemento no pasa a Completada
/// (lo revisa `GestionService.CambiarEstado`), así que un cierre siempre trae algo que enseñar.
/// </summary>
/// <param name="ActividadId">Elemento del calendario al que pertenece.</param>
/// <param name="NombreArchivo">Nombre original del archivo que subió quien cerró (el que se muestra y el que se descarga).</param>
/// <param name="Ruta">Nombre interno en disco: GUID + extensión de la lista blanca. Nunca se arma con el nombre del usuario.</param>
/// <param name="Tipo">Tipo MIME con el que se sirve (el calculado por la extensión, no el que envió el navegador).</param>
/// <param name="Tamano">Bytes guardados.</param>
/// <param name="SubidoPor">Cuenta que adjuntó el archivo.</param>
/// <param name="Subido">Momento del adjunto (coincide con el cierre, salvo que se adjunte en otra entrega).</param>
public record Evidencia(
    long Id,
    long ActividadId,
    string NombreArchivo,
    string Ruta,
    string Tipo,
    long Tamano,
    string SubidoPor,
    DateTime Subido)
{
    /// Con `image` al principio el navegador puede mostrarla como miniatura dentro de la tarjeta.
    public bool EsImagen => Tipo.StartsWith("image", StringComparison.OrdinalIgnoreCase);
}

/// Reglas de qué se acepta como evidencia. La petición fue «cuando el ingeniero la complete debe
/// poner la evidencia ya sea foto o archivo»: se aceptan fotos y los documentos de oficina más
/// comunes, con un tope por archivo y por envío para que la subida no congele la sesión.
public static class EvidenciaRules
{
    public const long MaxBytesPorArchivo = 20 * 1024 * 1024;
    public const int MaxArchivosPorEnvio = 5;

    /// Extensión (en minúsculas, con punto) → tipo MIME con el que se sirve. Todo lo que no esté
    /// aquí se rechaza: la lista es a la vez el filtro de seguridad (nada ejecutable entra a disco).
    private static readonly Dictionary<string, string> Tipos = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".bmp"] = "image/bmp", [".webp"] = "image/webp",
        [".tif"] = "image/tiff", [".tiff"] = "image/tiff",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint", [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain; charset=utf-8", [".csv"] = "text/csv; charset=utf-8", [".log"] = "text/plain; charset=utf-8",
        [".zip"] = "application/zip", [".rar"] = "application/vnd.rar", [".7z"] = "application/x-7z-compressed",
    };

    public static bool ExtensionPermitida(string? nombreArchivo) =>
        !string.IsNullOrWhiteSpace(nombreArchivo) && Tipos.ContainsKey(Extension(nombreArchivo));

    /// Tipo MIME de la extensión. Si no está en la lista devuelve `application/octet-stream`
    /// (se descarga, no se abre en el navegador).
    public static string TipoDe(string? nombreArchivo) =>
        nombreArchivo is not null && Tipos.TryGetValue(Extension(nombreArchivo), out var t) ? t : "application/octet-stream";

    public static string Extension(string? nombreArchivo) =>
        Path.GetExtension(nombreArchivo ?? "").ToLowerInvariant();

    /// Deja solo el nombre del archivo (sin rutas) y con un largo razonable para la columna.
    public static string SoloNombre(string? nombreArchivo)
    {
        var n = Path.GetFileName((nombreArchivo ?? "").Replace('\\', '/').Trim());
        if (n.Length > 150) n = n[..150];
        return n.Length == 0 ? "archivo" : n;
    }

    /// Valida un archivo recibido: que venga, que no pese de más, que su extensión esté en la lista
    /// y que su firma coincida con lo que dice ser (una `.jpg` que en realidad es un `.exe` se rechaza).
    public static string? ArchivoError(string nombreArchivo, byte[]? contenido)
    {
        if (contenido is null || contenido.Length == 0) return $"El archivo «{nombreArchivo}» llegó vacío.";
        if (contenido.LongLength > MaxBytesPorArchivo) return $"«{nombreArchivo}» pasa de 20 MB.";
        if (!ExtensionPermitida(nombreArchivo))
            return $"«{nombreArchivo}» no es una foto ni un documento aceptado (jpg, png, gif, bmp, webp, tif, pdf, doc, docx, xls, xlsx, ppt, pptx, txt, csv, log, zip, rar o 7z).";
        return FirmaCoincide(nombreArchivo, contenido) ? null : $"«{nombreArchivo}» no es realmente lo que su nombre dice; vuelve a adjuntarlo.";
    }

    private static bool FirmaCoincide(string nombreArchivo, byte[] c)
    {
        static bool Empieza(byte[] c, ReadOnlySpan<byte> firma) => c.Length >= firma.Length && c.AsSpan(0, firma.Length).SequenceEqual(firma);
        switch (Extension(nombreArchivo))
        {
            case ".jpg" or ".jpeg": return c.Length > 3 && c[0] == 0xFF && c[1] == 0xD8 && c[2] == 0xFF;
            case ".png": return Empieza(c, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            case ".gif": return Empieza(c, "GIF8"u8);
            case ".bmp": return Empieza(c, "BM"u8);
            case ".webp": return c.Length > 11 && Empieza(c, "RIFF"u8) && c.AsSpan(8, 4).SequenceEqual("WEBP"u8);
            case ".tif" or ".tiff": return Empieza(c, new byte[] { 0x49, 0x49, 0x2A, 0x00 }) || Empieza(c, new byte[] { 0x4D, 0x4D, 0x00, 0x2A });
            case ".pdf": return Empieza(c, "%PDF-"u8);
            case ".doc" or ".xls" or ".ppt": return Empieza(c, new byte[] { 0xD0, 0xCF, 0x11, 0xE0 });
            // docx/xlsx/pptx/zip comparten la firma ZIP; txt, csv y log son texto plano sin firma.
            case ".docx" or ".xlsx" or ".pptx" or ".zip": return Empieza(c, new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            case ".rar": return Empieza(c, "Rar!"u8);
            case ".7z": return Empieza(c, new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C });
            default: return true;
        }
    }
}
