using Meditechnosys.Intranet.Dicom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meditechnosys.Intranet.Dicom.Controllers;

[ApiController]
[Authorize(Policy = "Ingenieria")]
[Route("api/dicom/[controller]")]
public class StudiesController : ControllerBase
{
    private readonly DicomQrService _dicom;
    private readonly DownloadJobService _jobs;
    private readonly ILogger<StudiesController> _logger;

    public StudiesController(DicomQrService dicom, DownloadJobService jobs, ILogger<StudiesController> logger)
    {
        _dicom = dicom;
        _jobs = jobs;
        _logger = logger;
    }

    [HttpGet("/api/dicom/status")]
    public ActionResult<ApiStatus> Status()
    {
        var local = _dicom.Local;
        return Ok(new ApiStatus
        {
            Status = "ok",
            LocalAeTitle = local.AeTitle,
            OutputDirectory = _dicom.OutputRoot,
            RetrieveMode = local.RetrieveMode,
            Nodes = _dicom.Nodes.ToList(),
            MaxSimultaneous = _jobs.MaxSimultaneas,
            Verify = local.VerificarDescargas,
            MaxRetries = local.ReintentosMaximos,
            ZipMaxStudiesPerPart = local.ZipMaxStudies,
            ZipMaxMBPerPart = local.ZipMaxMBPorParte,
            ActiveJobs = _jobs.TrabajosVivos()
        });
    }

    [HttpGet]
    public async Task<ActionResult<List<StudyInfo>>> Search(
        [FromQuery] string? patientId = null,
        [FromQuery] string? patientName = null,
        [FromQuery] string? studyDate = null,
        [FromQuery] string? accessionNumber = null,
        [FromQuery] string? modalitiesInStudy = null,
        [FromQuery] string? studyDescription = null,
        [FromQuery] string? aeTitles = null,
        CancellationToken ct = default)
    {
        var criteria = new SearchRequest
        {
            PatientId = patientId,
            PatientName = patientName,
            StudyDate = studyDate,
            AccessionNumber = accessionNumber,
            ModalitiesInStudy = modalitiesInStudy,
            StudyDescription = studyDescription,
            AeTitles = SplitAeTitles(aeTitles)
        };

        if (DicomQrService.SinCriterio(criteria))
            return BadRequest(new { error = "Indica al menos un criterio de búsqueda (ID, nombre, fecha, etc.)" });

        return await ConsultarAsync(criteria, ct);
    }

    [HttpPost("search")]
    public async Task<ActionResult<List<StudyInfo>>> SearchPost(
        [FromBody] SearchRequest criteria,
        CancellationToken ct = default)
    {
        criteria ??= new SearchRequest();
        if (DicomQrService.SinCriterio(criteria))
            return BadRequest(new { error = "Indica al menos un criterio de búsqueda (ID, nombre, fecha, etc.)" });

        return await ConsultarAsync(criteria, ct);
    }

    /// <summary>
    /// C-FIND para los dos verbos (GET y POST). Un nodo que no responde NO es «0 estudios»: si la
    /// consulta viene vacía pero hubo nodos con fallo, se devuelve 502 con el motivo para que la
    /// pantalla lo escriba en rojo en el registro en vez de dar a entender que ese día no hubo nada.
    /// </summary>
    private async Task<ActionResult<List<StudyInfo>>> ConsultarAsync(SearchRequest criteria, CancellationToken ct)
    {
        try
        {
            var errores = new System.Collections.Concurrent.ConcurrentBag<string>();
            var results = await _dicom.SearchAsync(criteria, ct, errores);
            if (results.Count == 0 && !errores.IsEmpty)
                return StatusCode(502, new
                {
                    error = "Los nodos PACS no respondieron; la tabla vacía no significa que no haya estudios",
                    detail = string.Join(" · ", errores.Distinct())
                });
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en C-FIND");
            return StatusCode(502, new { error = "Error consultando el PACS", detail = ex.Message });
        }
    }

    /// <summary>
    /// Descarga síncrona (compatibilidad). Espera a que termine todo.
    /// Preferir POST /download/start + polling de progreso.
    /// </summary>
    [HttpPost("download")]
    public async Task<ActionResult<DownloadResult>> Download(
        [FromBody] DownloadRequest request,
        CancellationToken ct = default)
    {
        var items = BuildItems(request);
        if (items.Count == 0)
            return BadRequest(new { error = "Indica al menos un StudyInstanceUid" });

        try
        {
            var result = await _dicom.DownloadAsync(items, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en C-GET/C-MOVE");
            return StatusCode(502, new { error = "Error descargando del PACS", detail = ex.Message });
        }
    }

    /// <summary>
    /// Inicia la descarga en segundo plano y devuelve un jobId. Puede haber varias a la vez: si no
    /// queda un hueco libre el trabajo queda en cola y arranca solo al terminar otra.
    /// Consultar progreso en GET /api/dicom/studies/download/{jobId}/progress
    /// </summary>
    [HttpPost("download/start")]
    public ActionResult<DownloadJobResponse> DownloadStart([FromBody] DownloadRequest request)
    {
        var items = BuildItems(request);
        if (items.Count == 0)
            return BadRequest(new { error = "Indica al menos un StudyInstanceUid" });

        var expected = items.Sum(i => i.ExpectedInstances ?? 0);
        var jobId = _jobs.CreateJob(items.Count, expected, EtiquetaDe(items));

        var sink = new DownloadProgressSink
        {
            OnMessage = msg => _jobs.AddMessage(jobId, msg),
            OnImagesChanged = n => _jobs.SetImages(jobId, n),
            OnStudyCompleted = () => _jobs.IncrementStudies(jobId),
            OnZipping = () => _jobs.MarkZipping(jobId),
            OnPhase = fase => _jobs.MarkPhase(jobId, fase)
        };

        var enCola = _jobs.Encolar(jobId, async ct =>
        {
            try
            {
                var result = await _dicom.DownloadAsync(items, ct, sink);
                _jobs.Complete(jobId, result);
            }
            catch (OperationCanceledException)
            {
                _jobs.MarkCancelled(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en descarga job {JobId}", jobId);
                _jobs.Fail(jobId, ex.Message);
            }
        });

        if (!enCola)
        {
            _jobs.Cancel(jobId);
            _logger.LogWarning("Descarga rechazada: ya hay {Vivos} trabajos vivos (tope {Tope})",
                _jobs.TrabajosVivos(), _dicom.Local.MaxTrabajosEnCola);
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = $"Hay muchos trabajos de descarga en curso o en cola ({_jobs.TrabajosVivos()}). " +
                        "Espera a que termine alguno y vuelve a intentarlo."
            });
        }

        _logger.LogInformation(
            "Descarga DICOM {JobId} aceptada: {Estudios} estudio(s) por {Usuario}",
            jobId, items.Count, User.Identity?.Name ?? "desconocido");

        return Accepted(new DownloadJobResponse
        {
            JobId = jobId,
            StatusUrl = $"/api/dicom/studies/download/{jobId}/progress",
            StartedNow = _jobs.TrabajosVivos() <= _jobs.MaxSimultaneas
        });
    }

    /// <summary>
    /// Trabajos de descarga que siguen vivos (en curso o en cola). La pantalla los retoma al abrir,
    /// para que recargar la página no deje una descarga sin barra de progreso ni botón de cancelar.
    /// </summary>
    [HttpGet("download/activos")]
    public ActionResult<List<DownloadProgress>> DownloadActivos() => Ok(_jobs.Activos());

    /// <summary>Estado y progreso de un trabajo de descarga.</summary>
    [HttpGet("download/{jobId}/progress")]
    public ActionResult<DownloadProgress> DownloadProgress(string jobId)
    {
        if (!_jobs.TryGet(jobId, out var progress) || progress == null)
            return NotFound(new { error = "Job no encontrado o expirado" });
        return Ok(progress);
    }

    /// <summary>
    /// Cancela un trabajo de descarga en curso. Se detienen los estudios que faltaban por pedir;
    /// las imágenes ya recibidas quedan en el servidor (no se genera ZIP).
    /// </summary>
    [HttpPost("download/{jobId}/cancel")]
    public ActionResult CancelDownload(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return BadRequest(new { error = "jobId vacío" });

        if (!_jobs.Cancel(jobId))
            return NotFound(new { error = "El trabajo no existe o ya terminó" });

        _logger.LogInformation("Descarga DICOM {JobId} cancelada por {Usuario}",
            jobId, User.Identity?.Name ?? "desconocido");

        return Ok(new { jobId, status = "cancelled" });
    }

    [HttpPost("download/{studyInstanceUid}")]
    public async Task<ActionResult<DownloadResult>> DownloadOne(
        string studyInstanceUid,
        [FromQuery] string? aeTitle = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studyInstanceUid))
            return BadRequest(new { error = "StudyInstanceUid vacío" });

        try
        {
            var result = await _dicom.DownloadAsync(
                new[] { new DownloadItem { StudyInstanceUid = studyInstanceUid, AeTitle = aeTitle } }, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en C-GET/C-MOVE");
            return StatusCode(502, new { error = "Error descargando del PACS", detail = ex.Message });
        }
    }

    /// <summary>
    /// Resumen corto del trabajo para que la lista de descargas en pantalla se pueda distinguir de
    /// un vistazo cuando hay varias a la vez ("12 estudios · NUÑEZ LOPES JUANA · ASYN02").
    /// </summary>
    internal static string EtiquetaDe(List<DownloadItem> items)
    {
        var pacientes = items
            .Select(i => (i.PatientName ?? "").Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var quien = pacientes.Count switch
        {
            0 => "",
            1 => pacientes[0],
            _ => $"{pacientes[0]} y {pacientes.Count - 1} paciente(s) más"
        };
        var nodos = items
            .Select(i => (i.SourceName ?? i.AeTitle ?? "").Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var donde = nodos.Count switch
        {
            0 => "",
            _ when nodos.Count <= 3 => string.Join("/", nodos),
            _ => string.Join("/", nodos.Take(2)) + $" y {nodos.Count - 2} más"
        };
        return string.Join(" · ", new[] { $"{items.Count} estudio(s)", quien, donde }
            .Where(s => !string.IsNullOrEmpty(s)));
    }

    private static List<DownloadItem> BuildItems(DownloadRequest? request)
    {
        var items = new List<DownloadItem>();
        if (request?.Items != null)
            items.AddRange(request.Items);
        if (request?.StudyInstanceUids != null)
            items.AddRange(request.StudyInstanceUids
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => new DownloadItem { StudyInstanceUid = u }));
        return items;
    }

    private static List<string>? SplitAeTitles(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
