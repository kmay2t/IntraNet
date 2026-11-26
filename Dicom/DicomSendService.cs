using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Microsoft.Extensions.Options;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>Resultado de un C-ECHO contra un nodo PACS.</summary>
public record EcoResultado(bool Ok, string Detalle);

/// <summary>Estado de UN archivo DICOM enviado por C-STORE.</summary>
public class ResultadoArchivo
{
    public string Archivo { get; set; } = "";
    /// <summary>Guardado | Advertencia | Error</summary>
    public string Estado { get; set; } = "Pendiente";
    public string Detalle { get; set; } = "";
}

/// <summary>
/// Etiquetas que la pantalla de envío deja corregir de un estudio antes de mandarlo.
/// Se escribe solo lo que trae valor: un campo vacío significa «déjalo como está en el archivo».
/// El nombre ya viene en formato DICOM («APELLIDOS^NOMBRE»); la pantalla es la que convierte
/// lo que se escribe «APELLIDOS, NOMBRE».
/// </summary>
public class EdicionEstudio
{
    /// <summary>(0010,0010) PatientName.</summary>
    public string? Nombre { get; set; }
    /// <summary>(0010,0020) PatientID.</summary>
    public string? Paciente { get; set; }
    /// <summary>(0008,0060) Modality, y con ella (0008,0061) ModalitiesInStudy.</summary>
    public string? Modalidad { get; set; }
    /// <summary>(0008,1030) StudyDescription.</summary>
    public string? Descripcion { get; set; }

    public bool Vacio =>
        string.IsNullOrWhiteSpace(Nombre) && string.IsNullOrWhiteSpace(Paciente) &&
        string.IsNullOrWhiteSpace(Modalidad) && string.IsNullOrWhiteSpace(Descripcion);
}

/// <summary>Resumen de un envío de archivos a un nodo destino.</summary>
public class ResultadoEnvio
{
    public int Total { get; set; }
    public int Guardadas { get; set; }
    public int Advertencias { get; set; }
    public int Errores { get; set; }
    /// <summary>Imágenes a las que se les cambió alguna etiqueta antes de enviarlas.</summary>
    public int Editadas { get; set; }
    public List<ResultadoArchivo> Archivos { get; } = new();
}

/// <summary>
/// Envío de imágenes DICOM hacia los nodos PACS (C-STORE), y reenvío de estudios de un nodo a otro.
///
/// Diferencia de AE Titles —y la razón de que este servicio exista aparte de <see cref="DicomQrService"/>:
/// <list type="bullet">
/// <item>Para <b>consultar y descargar</b> (C-FIND / C-MOVE) se usa <c>Local:AeTitle</c> = <b>MEDI</b>,
/// que ya está registrado en los PACS y es el destino del C-MOVE (receptor en <c>Local:MovePort</c>).</item>
/// <item>Para <b>enviar imágenes</b> (C-STORE) se usa <c>Local:SendAeTitle</c> = <b>Medimport</b>,
/// que es el emisor de la imagen que el PACS destino debe aceptar.</item>
/// </list>
/// Los destinos posibles son los mismos nodos de <c>PacsNodes</c> (ASYN02/03/07/04): no hay un servidor
/// CMR aparte ni comparación entre servidores; se trae y se envía entre los mismos PACS de la intranet.
/// </summary>
public class DicomSendService
{
    private readonly DicomQrService _qr;
    private readonly LocalOptions _local;
    private readonly ILogger<DicomSendService> _logger;

    public DicomSendService(DicomQrService qr, IOptions<LocalOptions> local, ILogger<DicomSendService> logger)
    {
        _qr = qr;
        _local = local.Value;
        _logger = logger;
    }

    /// <summary>AE Title emisor de las imágenes que se envían (Medimport).</summary>
    public string SendAeTitle => string.IsNullOrWhiteSpace(_local.SendAeTitle) ? "Medimport" : _local.SendAeTitle.Trim();

    /// <summary>Nodos a los que se puede enviar (los mismos de donde se descarga).</summary>
    public IReadOnlyList<PacsNode> Destinos => _qr.Nodes;

    /// <summary>Resuelve un AE Title / nombre de nodo contra PacsNodes.</summary>
    public PacsNode? ResolverNodo(string? aeTitle)
    {
        if (string.IsNullOrWhiteSpace(aeTitle)) return null;
        var w = aeTitle.Trim();
        return _qr.Nodes.FirstOrDefault(n =>
            string.Equals(n.AeTitle, w, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.Name, w, StringComparison.OrdinalIgnoreCase));
    }

    public List<PacsNode> ResolverNodos(IEnumerable<string>? aeTitles) =>
        (aeTitles ?? Enumerable.Empty<string>())
            .Select(ResolverNodo)
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct()
            .ToList();

    private IDicomClient CreateSendClient(PacsNode node) =>
        DicomClientFactory.Create(node.Host, node.Port, false, SendAeTitle, node.AeTitle);

    private IDicomClient CreateQrClient(PacsNode node) =>
        DicomClientFactory.Create(node.Host, node.Port, false, _local.AeTitle, node.AeTitle);

    /// <summary>
    /// C-ECHO contra un nodo. Con <paramref name="emisor"/> = true usa <b>Medimport</b> (lo que hay que
    /// probar antes de enviar imágenes); con false usa <b>MEDI</b>, el AE con el que se consulta y se
    /// recibe el C-MOVE — es el que debe estar registrado en el servidor de origen de un reenvío.
    /// Si falla, casi siempre es que el AE no está dado de alta en ese PACS.
    /// </summary>
    public async Task<EcoResultado> EchoAsync(PacsNode node, bool emisor, CancellationToken ct)
    {
        var ae = emisor ? SendAeTitle : _local.AeTitle;
        try
        {
            var client = emisor ? CreateSendClient(node) : CreateQrClient(node);
            DicomStatus? status = null;
            await client.AddRequestAsync(new DicomCEchoRequest { OnResponseReceived = (_, r) => status = r.Status });
            await client.SendAsync(ct);

            return status == DicomStatus.Success
                ? new EcoResultado(true, $"{node.AeTitle} respondió a {ae} en {node.Host}:{node.Port}")
                : new EcoResultado(false, $"{node.AeTitle} respondió a {ae}: {status ?? DicomStatus.ProcessingFailure}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "C-ECHO fallido contra {Node} como {Ae}", node.Name, ae);
            return new EcoResultado(false, $"{ae} no pudo conectar con {node.Name} ({node.Host}:{node.Port}): {ex.Message}");
        }
    }

    /// <summary>
    /// Envía archivos DICOM por C-STORE a un nodo, en lotes de <c>Local:BatchSize</c> por asociación.
    /// Reporta cada archivo al terminar (para la tabla de la pantalla) y sigue aunque algunos fallen.
    ///
    /// <paramref name="ediciones"/> va indexada por StudyInstanceUID: la pantalla permite corregir
    /// nombre, ID de paciente, modalidad y descripción de un estudio antes de subirlo, y eso se
    /// escribe en el dataset de cada imagen al abrirla, justo antes de armar el C-STORE. Los UIDs
    /// (estudio, serie e imagen) NO se tocan: el estudio sigue siendo el mismo para el PACS.
    /// </summary>
    public async Task<ResultadoEnvio> StoreFilesAsync(
        PacsNode destino,
        IReadOnlyList<string> rutas,
        Action<ResultadoArchivo>? onUno,
        CancellationToken ct,
        IReadOnlyDictionary<string, EdicionEstudio>? ediciones = null)
    {
        var resumen = new ResultadoEnvio { Total = rutas.Count };
        var batchSize = Math.Max(1, _local.BatchSize);

        for (var i = 0; i < rutas.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var lote = rutas.Skip(i).Take(batchSize).ToList();
            await StoreBatchAsync(destino, lote, resumen, onUno, ct, ediciones);
        }
        return resumen;
    }

    /// <summary>Longitudes máximas de las etiquetas que se editan (VR LO/SH/CS de DICOM).</summary>
    private static string Recorta(string? valor, int max)
    {
        var v = (valor ?? "").Trim();
        return v.Length <= max ? v : v[..max];
    }

    /// <summary>Escribe en el dataset las etiquetas corregidas de un estudio.</summary>
    internal static int AplicarEdicion(DicomDataset ds, EdicionEstudio ed)
    {
        var hechas = 0;

        var nombre = Recorta(ed.Nombre, 64);
        if (nombre.Length > 0) { ds.AddOrUpdate(DicomTag.PatientName, nombre); hechas++; }

        var paciente = Recorta(ed.Paciente, 64);
        if (paciente.Length > 0) { ds.AddOrUpdate(DicomTag.PatientID, paciente); hechas++; }

        var descripcion = Recorta(ed.Descripcion, 64);
        if (descripcion.Length > 0) { ds.AddOrUpdate(DicomTag.StudyDescription, descripcion); hechas++; }

        var modality = Recorta(ed.Modalidad, 16).ToUpperInvariant();
        if (modality.Length > 0)
        {
            ds.AddOrUpdate(DicomTag.Modality, modality);
            ds.AddOrUpdate(DicomTag.ModalitiesInStudy, modality);
            hechas++;
        }
        return hechas;
    }

    private async Task StoreBatchAsync(
        PacsNode destino, List<string> lote, ResultadoEnvio resumen,
        Action<ResultadoArchivo>? onUno, CancellationToken ct,
        IReadOnlyDictionary<string, EdicionEstudio>? ediciones)
    {
        var pendientes = new List<(ResultadoArchivo Item, DicomCStoreRequest Request)>();
        var respondidos = new HashSet<ResultadoArchivo>();
        var gate = new object();

        // 1) Abrir y validar cada archivo del lote (los que no son DICOM no se intentan ni enviar).
        foreach (var ruta in lote)
        {
            ct.ThrowIfCancellationRequested();
            var item = new ResultadoArchivo { Archivo = Path.GetFileName(ruta) };
            // Se registra en el mismo orden de llegada para que quien llama pueda emparejar
            // resultado con archivo por posición (los nombres reales los pone la pantalla).
            resumen.Archivos.Add(item);
            try
            {
                var file = await DicomFile.OpenAsync(ruta, FileReadOption.ReadAll);
                var sopClass = file.Dataset.GetSingleValueOrDefault(DicomTag.SOPClassUID, "");
                var sopInstance = file.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, "");
                if (string.IsNullOrWhiteSpace(sopClass) || string.IsNullOrWhiteSpace(sopInstance))
                {
                    Cerrar(item, "Error", "No tiene SOPClassUID/SOPInstanceUID válidos.", resumen, onUno);
                    continue;
                }
                var current = item;
                // Corrección de etiquetas antes de armar el C-STORE (el request guarda una referencia
                // al dataset, así que hay que escribir aquí, no después).
                if (ediciones is not null && ediciones.Count > 0)
                {
                    var studyUid = file.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
                    if (studyUid.Length > 0 && ediciones.TryGetValue(studyUid, out var ed) && ed is not null &&
                        AplicarEdicion(file.Dataset, ed) > 0)
                        resumen.Editadas++;
                }
                var request = new DicomCStoreRequest(file)
                {
                    OnResponseReceived = (_, resp) =>
                    {
                        lock (gate) respondidos.Add(current);
                        switch (resp.Status.State)
                        {
                            case DicomState.Success:
                                Cerrar(current, "Guardado", "", resumen, onUno);
                                break;
                            case DicomState.Warning:
                                Cerrar(current, "Advertencia", resp.Status.ToString(), resumen, onUno);
                                break;
                            default:
                                Cerrar(current, "Error", $"El PACS rechazó la imagen: {resp.Status}", resumen, onUno);
                                break;
                        }
                    }
                };
                pendientes.Add((item, request));
            }
            catch (Exception ex)
            {
                Cerrar(item, "Error", $"No es un archivo DICOM válido: {ex.Message}", resumen, onUno);
            }
        }

        if (pendientes.Count == 0) return;

        // 2) Una sola asociación para todo el lote.
        try
        {
            var client = CreateSendClient(destino);
            foreach (var (item, request) in pendientes)
            {
                item.Estado = "Enviando";
                onUno?.Invoke(item);
                await client.AddRequestAsync(request);
            }
            await client.SendAsync(ct);
        }
        catch (OperationCanceledException)
        {
            MarcarSinRespuesta(pendientes, respondidos, gate, "Cancelado antes de recibir respuesta.", resumen, onUno);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "C-STORE contra {Node} cayó a medias", destino.Name);
            MarcarSinRespuesta(pendientes, respondidos, gate,
                $"Error de comunicación con {destino.AeTitle}: {ex.Message}", resumen, onUno);
            return;
        }

        // 3) Sin respuesta del PACS = no se puede dar por guardada.
        MarcarSinRespuesta(pendientes, respondidos, gate,
            $"{destino.AeTitle} no devolvió respuesta para esta imagen.", resumen, onUno);
    }

    private static void MarcarSinRespuesta(
        List<(ResultadoArchivo Item, DicomCStoreRequest Request)> pendientes,
        HashSet<ResultadoArchivo> respondidos, object gate,
        string detalle, ResultadoEnvio resumen, Action<ResultadoArchivo>? onUno)
    {
        foreach (var (item, _) in pendientes)
        {
            bool ok;
            lock (gate) ok = respondidos.Contains(item);
            if (!ok) Cerrar(item, "Error", detalle, resumen, onUno);
        }
    }

    private static void Cerrar(
        ResultadoArchivo item, string estado, string detalle,
        ResultadoEnvio resumen, Action<ResultadoArchivo>? onUno)
    {
        item.Estado = estado;
        item.Detalle = detalle;
        switch (estado)
        {
            case "Guardado": resumen.Guardadas++; break;
            case "Advertencia": resumen.Advertencias++; break;
            case "Error": resumen.Errores++; break;
        }
        onUno?.Invoke(item);
    }

    // ---------------------------------------------------------------- Reenvío

    /// <summary>
    /// Trae estudios de su nodo de origen (C-MOVE hacia el receptor MEDI) y los vuelve a enviar por
    /// C-STORE (como Medimport) a cada nodo destino. Se hace estudio por estudio: así el PACS de origen
    /// no recibe varias peticiones grandes a la vez y los archivos temporales caben en disco.
    /// Al terminar cada estudio se borra su carpeta temporal; si hubo errores se conserva y se avisa su ruta.
    /// </summary>
    public async Task ReenviarAsync(
        IReadOnlyList<DownloadItem> estudios,
        IReadOnlyList<PacsNode> destinos,
        ReenvioContexto ctx,
        CancellationToken ct)
    {
        if (destinos.Count == 0)
            throw new InvalidOperationException("Elige al menos un nodo destino.");

        // Cada trabajo tiene su rincón dentro de la carpeta de descargas, para no mezclar archivos
        // con otras descargas y poder borrarlo completo al terminar.
        var sub = $"reenvio/{ctx.JobId}";
        var raiz = Path.Combine(_qr.OutputRoot, sub);

        ctx.Mensaje($"Reenviando {estudios.Count} estudio(s) a " +
                    $"{string.Join(", ", destinos.Select(d => d.Name))} · emisor {SendAeTitle} · receptor {_local.AeTitle}:{_local.MovePort}");

        foreach (var est in estudios)
        {
            ct.ThrowIfCancellationRequested();
            var origen = _qr.Nodes.FirstOrDefault(n =>
                string.Equals(n.AeTitle, est.AeTitle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Name, est.AeTitle, StringComparison.OrdinalIgnoreCase));
            var nombreOrigen = origen?.Name ?? est.AeTitle ?? "origen";

            ctx.EstadoActual(est.StudyInstanceUid, "Descargando", $"De {nombreOrigen}…");
            ctx.Fase = "descargando";
            ctx.EstudioActual = est.StudyInstanceUid;

            var archivos = new List<string>();
            try
            {
                var sink = new DownloadProgressSink
                {
                    OnMessage = ctx.Mensaje,
                    OnImagesChanged = n => ctx.ImagenesDescargadas = n
                };
                // verificar: false — el reenvío llama a esto estudio por estudio y la comprobación
                // con asientado + C-FIND de conteo añadiría segundos a cada estudio reenviado. Aquí
                // basta con lo que ya hace este servicio: si no llegó ninguna imagen, se marca Error.
                var res = await _qr.DownloadAsync(new[] { est }, ct, sink, zip: false, subRoot: sub, verificar: false);
                if (res.ImagesSaved == 0)
                {
                    ctx.EstadoActual(est.StudyInstanceUid, "Error",
                        $"El origen {nombreOrigen} no entregó ninguna imagen. Revisa que el AE " +
                        $"{_local.AeTitle} esté registrado en {nombreOrigen} con la IP y el puerto {_local.MovePort} de este servidor.");
                    ctx.Errores++;
                    continue;
                }

                archivos = Directory.Exists(raiz)
                    ? Directory.EnumerateFiles(raiz, "*.dcm", SearchOption.AllDirectories).ToList()
                    : new List<string>();

                var esperado = est.ExpectedInstances ?? 0;
                var aviso = esperado > 0 && archivos.Count < esperado
                    ? $" (llegaron {archivos.Count} de {esperado} que anunciaba {nombreOrigen})"
                    : "";

                var enviadasTotal = 0;
                var erroresTotal = 0;
                var primerError = "";

                foreach (var destino in destinos)
                {
                    ct.ThrowIfCancellationRequested();
                    ctx.Fase = "enviando";
                    ctx.EstadoActual(est.StudyInstanceUid, "Enviando",
                        $"{archivos.Count} imagen(es) hacia {destino.Name}…");

                    var r = await StoreFilesAsync(destino, archivos, uno =>
                    {
                        if (uno.Estado is "Guardado" or "Advertencia") ctx.ImagenesEnviadas++;
                    }, ct);

                    enviadasTotal += r.Guardadas + r.Advertencias;
                    erroresTotal += r.Errores;
                    if (string.IsNullOrEmpty(primerError))
                        primerError = r.Archivos.FirstOrDefault(a => a.Estado == "Error")?.Detalle ?? "";
                }

                ctx.IncrementarEstudio();

                if (erroresTotal == 0 && aviso.Length == 0)
                {
                    ctx.EstadoActual(est.StudyInstanceUid, "Completado",
                        $"{enviadasTotal / destinos.Count} imagen(es) en {string.Join(" y ", destinos.Select(d => d.Name))}.");
                    Limpiar(raiz);
                }
                else
                {
                    ctx.Errores++;
                    ctx.EstadoActual(est.StudyInstanceUid, "Con errores",
                        $"{enviadasTotal} enviadas, {erroresTotal} con error{aviso}. {primerError} " +
                        $"(los archivos quedaron en {raiz} por si hace falta reintentar)");
                }
            }
            catch (OperationCanceledException)
            {
                ctx.EstadoActual(est.StudyInstanceUid, "Cancelado", "Se detuvo antes de terminar este estudio.");
                throw;
            }
            catch (Exception ex)
            {
                ctx.Errores++;
                _logger.LogError(ex, "Reenvío del estudio {Uid} falló", est.StudyInstanceUid);
                ctx.EstadoActual(est.StudyInstanceUid, "Error", ex.Message);
            }
        }

        // Si todo salió bien la carpeta temporal ya está vacía; si no, se deja para revisarla.
        if (ctx.Errores == 0) Limpiar(Path.Combine(_qr.OutputRoot, "reenvio"), soloSiVacia: true);
    }

    private static void Limpiar(string ruta, bool soloSiVacia = false)
    {
        try
        {
            if (!Directory.Exists(ruta)) return;
            if (soloSiVacia && Directory.EnumerateFileSystemEntries(ruta).Any()) return;
            Directory.Delete(ruta, true);
        }
        catch
        {
            // Limpieza opcional: el depurador de descargas la retira más tarde.
        }
    }
}

/// <summary>
/// Bitácora viva de un reenvío: la pantalla la consulta por polling. Los contadores y los estados
/// por estudio se leen desde otra petición, así que todo se protege con <see cref"_lock"/>.
/// </summary>
public class ReenvioContexto
{
    private readonly object _lock = new();
    private readonly List<string> _mensajes = new();

    public ReenvioContexto(string jobId, int estudios)
    {
        JobId = jobId;
        EstudiosTotal = estudios;
    }

    public string JobId { get; }
    public int EstudiosTotal { get; }
    public int EstudiosHechos;
    public int ImagenesDescargadas;
    public int ImagenesEnviadas;
    public int Errores;
    public string Fase = "descargando";
    public string EstudioActual = "";

    /// <summary>StudyInstanceUID → [estado, detalle] para pintar la tabla de resultados.</summary>
    private readonly Dictionary<string, string[]> _estados = new(StringComparer.Ordinal);

    public void Mensaje(string msg)
    {
        lock (_lock)
        {
            _mensajes.Add(msg);
            if (_mensajes.Count > 200) _mensajes.RemoveRange(0, _mensajes.Count - 150);
        }
    }

    public void EstadoActual(string uid, string estado, string detalle)
    {
        lock (_lock) _estados[uid] = new[] { estado, detalle };
        Mensaje($"{uid}: {estado} — {detalle}");
    }

    public void IncrementarEstudio() => Interlocked.Increment(ref EstudiosHechos);

    public ReenvioProgreso Copia()
    {
        lock (_lock)
        {
            return new ReenvioProgreso
            {
                JobId = JobId,
                EstudiosTotal = EstudiosTotal,
                EstudiosHechos = EstudiosHechos,
                ImagenesDescargadas = ImagenesDescargadas,
                ImagenesEnviadas = ImagenesEnviadas,
                Errores = Errores,
                Fase = Fase,
                EstudioActual = EstudioActual,
                Mensajes = _mensajes.ToList(),
                Estados = _estados.ToDictionary(kv => kv.Key, kv => kv.Value)
            };
        }
    }
}

/// <summary>Lo que devuelve GET /api/dicom/enviar/reenvio/{jobId}/progreso.</summary>
public class ReenvioProgreso
{
    public string JobId { get; set; } = "";
    public string Estado { get; set; } = "pending";
    public string Error { get; set; } = "";
    public int EstudiosTotal { get; set; }
    public int EstudiosHechos { get; set; }
    public int ImagenesDescargadas { get; set; }
    public int ImagenesEnviadas { get; set; }
    public int Errores { get; set; }
    public int Porcentaje { get; set; }
    public string Fase { get; set; } = "";
    public string EstudioActual { get; set; } = "";
    public List<string> Mensajes { get; set; } = new();
    public Dictionary<string, string[]> Estados { get; set; } = new();
}
