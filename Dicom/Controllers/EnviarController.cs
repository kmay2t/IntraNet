using Meditechnosys.Intranet.Dicom;
using Meditechnosys.Intranet.Models;
using Meditechnosys.Intranet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Meditechnosys.Intranet.Dicom.Controllers;

/// <summary>
/// Backend de la pantalla «Envío de estudios DICOM» (/dicom/enviar/).
///
/// Dos operaciones, con dos permisos distintos a propósito:
/// <list type="bullet">
/// <item><b>dicom.enviar</b> — subir archivos DICOM de la PC al PACS destino (C-STORE con el AE
/// emisor <c>Local:SendAeTitle</c>, por defecto <b>Medimport</b>).</item>
/// <item><b>dicom.reenviar</b> — traer estudios de otro nodo PACS y volverlos a enviar. Mueve
/// imágenes *entre* servidores, así que solo lo abren los perfiles a los que se les otorga.</item>
/// </list>
/// Para consultar y descargar se usa el AE <c>Local:AeTitle</c> (<b>MEDI</b>), el mismo del visor;
/// no existe un servidor CMR aparte ni comparación entre servidores: los orígenes y los destinos
/// son los mismos nodos de <c>PacsNodes</c> (ASYN02/03/07/04).
/// </summary>
[ApiController]
[Authorize(Policy = "Ingenieria")]
[Route("api/dicom/enviar")]
public class EnviarController : ControllerBase
{
    /// <summary>Tope de un lote de subida. La pantalla manda los archivos en tandas de este tamaño.</summary>
    private const long LimiteMB = 400;

    private readonly DicomSendService _send;
    private readonly DicomQrService _qr;
    private readonly ReenvioJobService _jobs;
    private readonly ILogger<EnviarController> _logger;

    public EnviarController(
        DicomSendService send, DicomQrService qr, ReenvioJobService jobs, ILogger<EnviarController> logger)
    {
        _send = send;
        _qr = qr;
        _jobs = jobs;
        _logger = logger;
    }

    private bool Puede(string clave) => User.HasClaim(GestionService.PermClaim, clave);

    // ------------------------------------------------------------------ común

    public record NodoDicom(string Name, string Host, int Port, string AeTitle);

    /// <summary>Nodos disponibles y AE Titles con los que se trabaja, para llenar los listboxes.</summary>
    [HttpGet("nodos")]
    [Authorize(Policy = "P:" + CatalogoPermisos.DicomEnviar)]
    public ActionResult<object> Nodos() => Ok(new
    {
        /// <summary>AE que emite las imágenes al enviar (C-STORE).</summary>
        emisor = _send.SendAeTitle,
        /// <summary>AE con el que se consulta y recibe (C-FIND / C-MOVE).</summary>
        consultor = _qr.Local.AeTitle,
        puertoRecepcion = _qr.Local.MovePort,
        lote = _qr.Local.BatchSize,
        limiteMB = LimiteMB,
        puedeReenviar = Puede(CatalogoPermisos.DicomReenviar),
        nodos = _send.Destinos.Select(n => new NodoDicom(n.Name, n.Host, n.Port, n.AeTitle)).ToList()
    });

    public class EcoRequest
    {
        public string? AeTitle { get; set; }
        /// <summary>
        /// true = probar como <b>emisor</b> (Medimport, antes de enviar imágenes al destino).
        /// false = probar como <b>consultor</b> (MEDI, antes de traer estudios de un origen).
        /// </summary>
        public bool Emisor { get; set; } = true;
    }

    /// <summary>C-ECHO contra el nodo elegido, con el AE que corresponda a lo que se va a hacer.</summary>
    [HttpPost("eco")]
    [Authorize(Policy = "P:" + CatalogoPermisos.DicomEnviar)]
    public async Task<ActionResult<object>> Eco([FromBody] EcoRequest? req, CancellationToken ct)
    {
        var nodo = _send.ResolverNodo(req?.AeTitle);
        if (nodo is null)
            return BadRequest(new { error = "Elige un nodo válido en la lista de servidores." });

        var comoEmisor = req?.Emisor ?? true;
        var r = await _send.EchoAsync(nodo, comoEmisor, ct);
        _logger.LogInformation("C-ECHO de {Rol} a {Node}: {Ok} por {Usuario}",
            comoEmisor ? "emisor" : "consultor", nodo.Name, r.Ok, User.Identity?.Name ?? "desconocido");

        return Ok(new
        {
            ok = r.Ok,
            detalle = r.Detalle,
            rol = comoEmisor ? "emisor" : "consultor",
            emisor = comoEmisor ? _send.SendAeTitle : _qr.Local.AeTitle,
            destino = nodo.AeTitle,
            direccion = $"{nodo.Host}:{nodo.Port}"
        });
    }

    // ------------------------------------------------------------ subir archivos

    /// <summary>
    /// Recibe una tanda de archivos DICOM del navegador, los guarda en una carpeta temporal del
    /// servidor y los envía por C-STORE al nodo destino. Devuelve el estado de cada archivo
    /// (Guardado / Advertencia / Error) con el mismo detalle que daba la app de escritorio.
    /// La carpeta temporal se borra siempre, haya salido o no.
    /// </summary>
    [HttpPost("archivos")]
    [Authorize(Policy = "P:" + CatalogoPermisos.DicomEnviar)]
    [RequestSizeLimit(LimiteMB * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = LimiteMB * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Subir(
        [FromForm] IFormFileCollection archivos,
        [FromForm] string? destino,
        [FromForm] string? nombres,
        [FromForm] string? ediciones,
        CancellationToken ct)
    {
        var nodo = _send.ResolverNodo(destino);
        if (nodo is null)
            return BadRequest(new { error = "Elige primero el nodo PACS destino en la lista." });

        if (archivos is null || archivos.Count == 0)
            return BadRequest(new { error = "No llegó ningún archivo." });

        var etiquetas = LeerEtiquetas(nombres, archivos);
        var parches = LeerEdiciones(ediciones);

        var carpeta = Path.Combine(AppContext.BaseDirectory, "dicom_envios", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);

        try
        {
            var rutas = new List<string>(archivos.Count);
            var visibles = new List<string>(archivos.Count);
            var rechazados = new List<(int Index, string Motivo)>();

            for (var i = 0; i < archivos.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var f = archivos[i];
                if (f.Length == 0)
                {
                    rechazados.Add((i, "El archivo está vacío."));
                    continue;
                }
                // Nombre en disco = índice: evita choques entre dos carpetas con archivos iguales.
                var destinoRuta = Path.Combine(carpeta, $"{i:D6}.dcm");
                try
                {
                    await using var fs = new FileStream(destinoRuta, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await f.CopyToAsync(fs, ct);
                    rutas.Add(destinoRuta);
                    visibles.Add(etiquetas[i]);
                }
                catch (Exception ex)
                {
                    rechazados.Add((i, $"No se pudo guardar en el servidor: {ex.Message}"));
                }
            }

            var resultado = rutas.Count > 0
                ? await _send.StoreFilesAsync(nodo, rutas, null, ct, parches)
                : new ResultadoEnvio();

            // Empareja cada resultado con su etiqueta original (llegan en el mismo orden).
            var filas = new List<object>(resultado.Archivos.Count);
            for (var i = 0; i < resultado.Archivos.Count; i++)
                filas.Add(new
                {
                    archivo = i < visibles.Count ? visibles[i] : resultado.Archivos[i].Archivo,
                    estado = resultado.Archivos[i].Estado,
                    detalle = resultado.Archivos[i].Detalle
                });
            foreach (var (idx, motivo) in rechazados)
                filas.Add(new { archivo = etiquetas[idx], estado = "Error", detalle = motivo });

            _logger.LogInformation("Envío DICOM de {Usuario}: {Ok}/{Tot} imágenes a {Nodo} como {Ae} ({Ed} con etiquetas corregidas)",
                User.Identity?.Name ?? "desconocido", resultado.Guardadas + resultado.Advertencias,
                archivos.Count, nodo.Name, _send.SendAeTitle, resultado.Editadas);

            return Ok(new
            {
                destino = nodo.Name,
                emisor = _send.SendAeTitle,
                total = archivos.Count,
                guardadas = resultado.Guardadas,
                advertencias = resultado.Advertencias,
                errores = resultado.Errores + rechazados.Count,
                editadas = resultado.Editadas,
                archivos = filas
            });
        }
        finally
        {
            try { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true); }
            catch (Exception ex) { _logger.LogWarning(ex, "No se pudo borrar la carpeta temporal de envío"); }
        }
    }

    /// <summary>
    /// Nombres que muestra la pantalla. Cuando se sube una carpeta, varios archivos pueden llamarse
    /// igual, así que el navegador manda la ruta relativa completa como etiqueta.
    /// </summary>
    private static string[] LeerEtiquetas(string? nombres, IFormFileCollection archivos)
    {
        var etiquetas = new string[archivos.Count];
        List<string>? lista = null;
        if (!string.IsNullOrWhiteSpace(nombres))
        {
            try { lista = System.Text.Json.JsonSerializer.Deserialize<List<string>>(nombres); }
            catch { lista = null; }
        }
        for (var i = 0; i < archivos.Count; i++)
        {
            var e = lista is not null && i < lista.Count ? lista[i] : null;
            if (string.IsNullOrWhiteSpace(e))
                e = Path.GetFileName(archivos[i].FileName);
            if (string.IsNullOrWhiteSpace(e))
                e = $"archivo {i + 1}";
            etiquetas[i] = e.Trim();
        }
        return etiquetas;
    }

    /// <summary>
    /// Correcciones de etiquetas que trae la tanda, en JSON indexado por StudyInstanceUID:
    /// <c>{"1.2.840...": {"nombre":"APELLIDO^NOMBRE","paciente":"123","modalidad":"CT","descripcion":".."}}</c>.
    /// Un campo vacío se ignora (deja la etiqueta como viene en el archivo). Lo que no se entienda
    /// se descarta en silencio: la subida no se rechaza por un JSON mal formado.
    /// </summary>
    private static IReadOnlyDictionary<string, EdicionEstudio>? LeerEdiciones(string? ediciones)
    {
        if (string.IsNullOrWhiteSpace(ediciones)) return null;
        try
        {
            var leidas = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, EdicionEstudio>>(
                ediciones, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (leidas is null) return null;
            var valida = leidas
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null && !kv.Value.Vacio)
                .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value!);
            return valida.Count > 0 ? valida : null;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- reenvío

    public class ReenviarRequest
    {
        /// <summary>Estudios a traer, cada uno con el nodo de donde se obtuvo en la búsqueda.</summary>
        public List<DownloadItem>? Items { get; set; }
        /// <summary>AE Titles / nombres de los nodos a los que se reenvían.</summary>
        public List<string>? Destinos { get; set; }
    }

    /// <summary>
    /// Inicia el reenvío en segundo plano: C-MOVE del origen al receptor MEDI de este servidor y
    /// C-STORE a cada destino como Medimport. Requiere el permiso dicom.reenviar.
    /// </summary>
    [HttpPost("reenviar")]
    [Authorize(Policy = "P:" + CatalogoPermisos.DicomReenviar)]
    public ActionResult<object> Reenviar([FromBody] ReenviarRequest? req)
    {
        var estudios = (req?.Items ?? new List<DownloadItem>())
            .Where(i => !string.IsNullOrWhiteSpace(i.StudyInstanceUid))
            .ToList();
        if (estudios.Count == 0)
            return BadRequest(new { error = "Marca al menos un estudio de la lista de resultados." });

        var destinos = _send.ResolverNodos(req?.Destinos);
        if (destinos.Count == 0)
            return BadRequest(new { error = "Elige el nodo destino en la lista." });

        // Un nodo no puede ser su propio destino: se enviaría a sí mismo lo que ya tiene.
        var desdeNodos = estudios
            .Select(s => s.AeTitle)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        destinos = destinos
            .Where(d => !desdeNodos.Contains(d.AeTitle) && !desdeNodos.Contains(d.Name))
            .ToList();
        if (destinos.Count == 0)
            return BadRequest(new {
                error = "El nodo destino no puede ser el mismo del que se traen los estudios. " +
                        "Cambia la lista «hacia dónde se envía»." });

        var (jobId, ctx, token) = _jobs.Crear(estudios.Count);

        _logger.LogInformation("Reenvío DICOM {JobId} de {Usuario}: {N} estudio(s) hacia {Destinos}",
            jobId, User.Identity?.Name ?? "desconocido", estudios.Count,
            string.Join(", ", destinos.Select(d => d.Name)));

        _ = Task.Run(async () =>
        {
            try
            {
                _jobs.MarcarCorriendo(jobId);
                await _send.ReenviarAsync(estudios, destinos, ctx, token);
                _jobs.Completar(jobId);
            }
            catch (OperationCanceledException)
            {
                // Cancelar() ya dejó el estado en "cancelled"; no lo reetiquetes como error.
                _logger.LogInformation("Reenvío DICOM {JobId} cancelado por {Usuario}",
                    jobId, User.Identity?.Name ?? "desconocido");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reenvío DICOM {JobId} falló", jobId);
                _jobs.Fallar(jobId, ex.Message);
            }
        }, CancellationToken.None);

        return Accepted(new { jobId, estado = "pending" });
    }

    /// <summary>Progreso de un reenvío (por estudios, no por imágenes: es lo que se puede medir).</summary>
    [HttpGet("reenviar/{jobId}/progreso")]
    [Authorize(Policy = "P:" + CatalogoPermisos.DicomReenviar)]
    public ActionResult<ReenvioProgreso> Progreso(string jobId) =>
        _jobs.TryGet(jobId, out var p) && p is not null
            ? Ok(p)
            : NotFound(new { error = "Trabajo no encontrado o ya expirado" });

    /// <summary>Detiene el reenvío: ya no se piden los estudios que faltaban.</summary>
    [HttpPost("reenviar/{jobId}/cancelar")]
    [Authorize(Policy = "P:" + CatalogoPermisos.DicomReenviar)]
    public ActionResult Cancelar(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return BadRequest(new { error = "jobId vacío" });
        if (!_jobs.Cancelar(jobId)) return NotFound(new { error = "El trabajo no existe o ya terminó" });
        return Ok(new { jobId, estado = "cancelled" });
    }
}
