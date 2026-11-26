using Meditechnosys.Intranet.Models;
using Meditechnosys.Intranet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meditechnosys.Intranet.Dicom.Controllers;

[ApiController]
[Route("api/dicom/worklist")]
[Authorize(Policy = "P:" + CatalogoPermisos.DicomVer)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorklistController(IWorklistService service, IDicomPruebaStore registros) : ControllerBase
{
    [HttpGet("opciones")]
    public IActionResult Options() => Ok(new
    {
        aeLocal = "MEDI",
        puedeEnviar = User.HasClaim(GestionService.PermClaim, CatalogoPermisos.DicomEnviar)
    });

    [HttpPost("buscar")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Search(WorklistSearchRequest request, CancellationToken ct)
    {
        try { return Ok(await service.SearchAsync(request, ct)); }
        catch (WorklistException ex)
        {
            return Problem(statusCode: ex.Timeout ? 504 : 502,
                title: "No se pudo consultar la lista de trabajo", detail: ex.Message);
        }
    }

    [HttpPost("eco")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Echo(WorklistConnection connection, CancellationToken ct) =>
        Ok(new { conectado = await service.EchoAsync(connection, ct) });

    [HttpGet("patron")]
    public IActionResult Pattern() => File(ContrastTestImage.PreviewBmp(), "image/bmp");

    [HttpPost("prueba")]
    [Authorize(Policy = "P:" + CatalogoPermisos.DicomEnviar)]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> SendTest(WorklistTestRequest request, CancellationToken ct)
    {
        if (!request.Confirmar)
            return Problem(statusCode: 400, title: "Confirma el envío de la imagen de prueba al destino elegido.");
        var resultado = await service.SendTestAsync(request, ct);
        // Del envío queda constancia en el servidor: la conexión usada, los tags que salieron en la imagen y
        // lo que el propio destino respondió al preguntarle por ese estudio. Es la lista que lee la pantalla.
        registros.Registrar(new DicomPrueba(0, DateTime.UtcNow, User.Identity?.Name ?? "desconocido",
            resultado.Estado, resultado.Mensaje, request.Conexion.Host.Trim(), request.Conexion.Puerto,
            request.Conexion.AeRemoto.Trim(), request.Conexion.AeLocal.Trim(),
            resultado.Tags, resultado.Campos, resultado.Verificacion));
        return Ok(resultado);
    }

    /// <summary>
    /// Las pruebas enviadas, de la más reciente hacia atrás, tal como quedaron registradas en el servidor.
    /// Responde NoStore y no se puede borrar desde la pantalla.
    /// </summary>
    [HttpGet("envios")]
    public IActionResult Sends(int limite = 50) => Ok(new { envios = registros.Listar(limite) });
}
