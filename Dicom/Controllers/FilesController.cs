using Meditechnosys.Intranet.Dicom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meditechnosys.Intranet.Dicom.Controllers;

[ApiController]
[Authorize(Policy = "Ingenieria")]
[Route("api/dicom/files")]
public class FilesController : ControllerBase
{
    private readonly DicomQrService _dicom;

    public FilesController(DicomQrService dicom)
    {
        _dicom = dicom;
    }

    /// <summary>
    /// Descarga un ZIP generado tras una recuperación DICOM.
    /// El navegador lo guarda en la carpeta Descargas de la PC del usuario.
    /// Buffer 1 MB + rangos HTTP: descarga rápida y reanudable.
    /// </summary>
    [HttpGet("{fileName}")]
    public IActionResult GetZip(string fileName)
    {
        // Solo nombres seguros (sin rutas)
        fileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains(".."))
        {
            return BadRequest(new { error = "Nombre de archivo no válido" });
        }

        var path = Path.Combine(_dicom.OutputRoot, "_zips", fileName);
        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "ZIP no encontrado o ya eliminado" });

        // Buffer 1 MB + SequentialScan + rangos: gestores (IDM/aria2) o varias conexiones
        // pueden bajar el mismo ZIP en paralelo y reanudar si se corta.
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1 * 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.AcceptRanges = "bytes";
        return File(stream, "application/zip", fileName, enableRangeProcessing: true);
    }
}
