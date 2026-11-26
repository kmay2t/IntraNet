using System.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Meditechnosys.Intranet.Dicom;
using Meditechnosys.Intranet.Services;
using Microsoft.Extensions.Options;

namespace Meditechnosys.Intranet.Dicom;

public class LocalOptions
{
    public string AeTitle { get; set; } = "MEDI";
    /// <summary>
    /// AE Title con el que esta intranet **envía** imágenes al PACS (C-STORE), es decir el emisor de
    /// la imagen. Va aparte de <see cref="AeTitle"/> porque para consultar y descargar (C-FIND/C-MOVE)
    /// el PACS ya reconoce a MEDI, y para recibir estudios nuevos se autoriza un AE propio del envío.
    /// Debe estar registrado en el PACS destino o rechazará la asociación.
    /// </summary>
    public string SendAeTitle { get; set; } = "Medimport";
    public string OutputDirectory { get; set; } = "dicom_downloads";
    public int MovePort { get; set; } = 11113;
    /// <summary>get | move | auto</summary>
    public string RetrieveMode { get; set; } = "move";
    /// <summary>Máximo de asociaciones simultáneas contra un mismo PACS, sumando TODAS las PC que descargan.</summary>
    public int MaxParallelPerNode { get; set; } = 3;
    /// <summary>
    /// Tamaño de lote para C-MOVE/C-GET masivo. Evita asociaciones eternas y permite
    /// actualizar progreso cada N estudios. Recomendado 25–100. Default 50.
    /// </summary>
    public int BatchSize { get; set; } = 50;
    /// <summary>
    /// Estudios máximos que caben en UNA parte de ZIP. Si la descarga trae más estudios que este
    /// número se generan varias partes numeradas (parte 1 de 3, 2 de 3…) en lugar de renunciar al
    /// ZIP como antes. Default 100. Pon 0 para que entre todo en un solo ZIP.
    /// </summary>
    public int ZipMaxStudies { get; set; } = 100;
    /// <summary>MB máximos por parte de ZIP (0 = sin tope de tamaño). Default 2048 MB.</summary>
    public int ZipMaxMBPorParte { get; set; } = 2048;
    /// <summary>Horas que se conservan los ZIP en _zips antes de borrarse (0 = nunca).</summary>
    public int ZipRetentionHours { get; set; } = 48;

    // ---------- Verificación de integridad ----------

    /// <summary>
    /// Al terminar cada descarga la intranet cuenta las imágenes que llegaron de cada estudio,
    /// las compara con las que anuncia el PACS y vuelve a pedir lo que falte. false = descargar y
    /// empaquetar sin comprobar.
    /// </summary>
    public bool VerificarDescargas { get; set; } = true;
    /// <summary>Cuántas veces se re-piden los estudios incompletos. 0 = solo avisar, no re-pedir.</summary>
    public int ReintentosMaximos { get; set; } = 2;
    /// <summary>
    /// Cuando el PACS no informó cuántas imágenes tiene un estudio, se le pregunta con un C-FIND
    /// antes de dar el estudio por bueno. Cuesta una consulta por estudio, así que tiene un tope.
    /// </summary>
    public bool VerificarConPacs { get; set; } = true;
    /// <summary>Tope de estudios a los que se les pregunta el conteo al PACS por descarga.</summary>
    public int VerificarConPacsMaxEstudios { get; set; } = 300;

    // ---------- Varias descargas a la vez ----------

    /// <summary>Descargas que se ejecutan simultáneamente; las que llegan después quedan en cola.</summary>
    public int MaxDescargasSimultaneas { get; set; } = 3;
    /// <summary>Trabajos máximos en cola antes de rechazar una descarga nueva.</summary>
    public int MaxTrabajosEnCola { get; set; } = 20;

    // ---------- Depuración interna de descargas (LimpiadorDescargas) ----------

    /// <summary>Activa el repaso automático de la carpeta de descargas. false = que nada se borre solo.</summary>
    public bool DepurarDescargas { get; set; } = true;
    /// <summary>Cada cuántos minutos revisa la carpeta. Default 60 (una hora).</summary>
    public int DepurarCadaMinutos { get; set; } = 60;
    /// <summary>Horas que se conserva un .dcm desde que se guardó; al cumplirse se borra en el siguiente repaso.</summary>
    public int ConservarHoras { get; set; } = 48;
    /// <summary>
    /// Tope de la carpeta de descargas en megabytes. Si se pasa —por una descarga masiva antes de
    /// la hora de repaso— se borra lo más viejo hasta quedar debajo. 0 = sin tope y, por tanto,
    /// nunca se borran archivos recientes solo para bajar tamaño. Default 0.
    /// </summary>
    public int MaxMB { get; set; }
    /// <summary>
    /// Espacio libre mínimo en GB para permitir la depuración. Si hay esta cantidad o más, el
    /// repaso no borra nada. 0 = limpiar siempre. Default 50 GB.
    /// </summary>
    public double EspacioLibreMinimoGB { get; set; } = 50;
}

/// <summary>Callbacks opcionales para reportar progreso durante la descarga.</summary>
public class DownloadProgressSink
{
    public Action<string>? OnMessage { get; set; }
    public Action<int>? OnImagesChanged { get; set; }
    public Action? OnStudyCompleted { get; set; }
    public Action? OnZipping { get; set; }
    /// <summary>Cambios de fase que la pantalla debe mostrar: verifying | retrying | zipping.</summary>
    public Action<string>? OnPhase { get; set; }
    /// <summary>Carpeta relativa (dentro de OutputDirectory) que esta descarga acaba de crear.</summary>
    public Action<string>? OnFolderCreated { get; set; }
}

/// <summary>
/// Estado aislado de UNA descarga. Cada computadora que descarga obtiene su propia sesión:
/// contadores, carpetas, mensajes, barra de progreso (sink) y ZIP nunca se mezclan con los de otra.
/// </summary>
public sealed class DownloadSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public DownloadResult Result { get; }
    public DownloadProgressSink? Sink { get; }
    /// <summary>≥200 estudios → modo silencioso (solo hitos y errores).</summary>
    public bool BulkQuiet { get; }
    public HashSet<string> StudyUids { get; }
    /// <summary>
    /// Subcarpeta propia dentro de OutputDirectory donde se guardan las imágenes de ESTA sesión.
    /// La usan los reenvíos para escribir en su propio rincón y poder borrarlo al terminar sin
    /// tocar los archivos de otras descargas. null = la raíz de descargas (comportamiento de siempre).
    /// </summary>
    public string? SubRoot { get; init; }

    private readonly object _msgLock = new();
    private readonly ConcurrentDictionary<string, byte> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _imagesByStudy = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// SOP InstanceUID **distintos** que se guardaron por estudio. Se lleva un hash de 64 bits por
    /// imagen para que una descarga de 100 000 archivos ocupe menos de 1 MB de RAM. Hace falta para
    /// comprobar la integridad: si un estudio se re-pide en un reintento, el PACS manda otra vez
    /// todas sus imágenes y el contador bruto las duplicaría, tapando las que de verdad faltan.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte>> _unicosByStudy =
        new(StringComparer.OrdinalIgnoreCase);

    private int _images;
    private int _completed;

    public DownloadSession(DownloadResult result, DownloadProgressSink? sink, bool bulkQuiet, IEnumerable<string> studyUids)
    {
        Result = result;
        Sink = sink;
        BulkQuiet = bulkQuiet;
        StudyUids = new HashSet<string>(studyUids, StringComparer.OrdinalIgnoreCase);
    }

    public int ImageCount => Volatile.Read(ref _images);
    public int StudiesCompleted => Volatile.Read(ref _completed);
    public IReadOnlyCollection<string> CreatedDirs => _dirs.Keys.ToList();

    /// <summary>true si la carpeta es nueva para esta sesión.</summary>
    public bool NoteDir(string key) => _dirs.TryAdd(key, 0);

    /// <summary>Cuenta una imagen de esta sesión y actualiza SU barra de progreso.</summary>
    public int IncrementImages(string studyUid, string sopUid)
    {
        if (!string.IsNullOrEmpty(studyUid))
        {
            _imagesByStudy.AddOrUpdate(studyUid, 1, (_, v) => v + 1);
            _unicosByStudy
                .GetOrAdd(studyUid, _ => new ConcurrentDictionary<long, byte>())
                .TryAdd(Hash64(sopUid), 0);
        }
        var n = Interlocked.Increment(ref _images);
        Sink?.OnImagesChanged?.Invoke(n);
        return n;
    }

    public int ImagesForStudies(IEnumerable<string> uids) =>
        uids.Sum(u => _imagesByStudy.TryGetValue(u, out var v) ? v : 0);

    /// <summary>Imágenes distintas que hay realmente de un estudio (lo que se puede comparar con el PACS).</summary>
    public int UniqueImagesForStudy(string studyUid) =>
        string.IsNullOrEmpty(studyUid) || !_unicosByStudy.TryGetValue(studyUid, out var s) ? 0 : s.Count;

    public Dictionary<string, int> UniqueImagesByStudy(IEnumerable<string> uids)
    {
        var salida = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in uids) salida[u] = UniqueImagesForStudy(u);
        return salida;
    }

    /// <summary>Total de imágenes distintas recibidas; se usa para saber si la transferencia ya se asentó.</summary>
    public int UniqueImagesTotal() => _unicosByStudy.Values.Sum(s => s.Count);

    /// <summary>Hash FNV-1a de 64 bits: determinista y barato, suficiente para contar distintos.</summary>
    internal static long Hash64(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return 0;
        var h = 1469598103934665603UL;
        foreach (var c in valor)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return unchecked((long)h);
    }

    public void CompleteStudy()
    {
        Interlocked.Increment(ref _completed);
        Sink?.OnStudyCompleted?.Invoke();
    }

    public void AddMsg(string msg, bool important = false)
    {
        // En modo masivo solo dejamos pasar hitos / errores / resúmenes
        if (BulkQuiet && !important)
        {
            if (msg.Contains("fallo", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Excepción", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("→ Registra", StringComparison.OrdinalIgnoreCase))
            {
                // pasa
            }
            else
                return;
        }

        lock (_msgLock)
        {
            Result.Messages.Add(msg);
            // Cap duro: no acumular cientos de miles de líneas en memoria
            if (Result.Messages.Count > 200)
                Result.Messages.RemoveRange(0, Result.Messages.Count - 150);
        }
        Sink?.OnMessage?.Invoke(msg);
    }

    public void Milestone(string msg) => AddMsg(msg, important: true);
}

public class DicomQrService : IDisposable
{
    private readonly List<PacsNode> _nodes;
    private readonly LocalOptions _local;
    private readonly ILogger<DicomQrService> _logger;
    private readonly string _outputRoot;

    // StudyInstanceUID -> sesiones activas que lo esperan (varias PC pueden pedir el mismo estudio)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DownloadSession>> _waiting =
        new(StringComparer.OrdinalIgnoreCase);

    // Tope de asociaciones simultáneas por PACS, COMPARTIDO entre todas las PC que descargan
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _nodeGates = new(StringComparer.OrdinalIgnoreCase);

    private IDicomServer? _scp;

    public DicomQrService(
        IEnumerable<PacsNode> pacsNodes,
        IOptions<LocalOptions> local,
        ILogger<DicomQrService> logger)
    {
        _nodes = pacsNodes?.ToList() ?? new List<PacsNode>();
        foreach (var n in _nodes)
            if (string.IsNullOrWhiteSpace(n.Name)) n.Name = n.AeTitle;

        _local = local.Value;
        if (_local.MaxParallelPerNode < 1) _local.MaxParallelPerNode = 1;
        if (_local.BatchSize < 1) _local.BatchSize = 50;
        if (_local.BatchSize > 500) _local.BatchSize = 500;
        if (_local.ReintentosMaximos < 0) _local.ReintentosMaximos = 0;
        if (_local.ReintentosMaximos > 5) _local.ReintentosMaximos = 5;
        if (_local.VerificarConPacsMaxEstudios < 0) _local.VerificarConPacsMaxEstudios = 0;
        if (_local.ZipMaxStudies < 0) _local.ZipMaxStudies = 0;
        if (_local.ZipMaxMBPorParte < 0) _local.ZipMaxMBPorParte = 0;
        if (_local.MaxDescargasSimultaneas < 1) _local.MaxDescargasSimultaneas = 1;
        if (_local.MaxDescargasSimultaneas > 8) _local.MaxDescargasSimultaneas = 8;
        if (_local.MaxTrabajosEnCola < 1) _local.MaxTrabajosEnCola = 1;
        _logger = logger;

        var baseDir = AppContext.BaseDirectory;
        _outputRoot = Path.IsPathRooted(_local.OutputDirectory)
            ? _local.OutputDirectory
            : Path.Combine(baseDir, _local.OutputDirectory);

        Directory.CreateDirectory(_outputRoot);
        StoreScp.Handler = OnIncomingDatasetAsync;
    }

    /// <summary>
    /// Abre UNA sola vez el receptor C-STORE (destino de los C-MOVE) mientras la app esté viva.
    /// Antes se abría/cerraba el puerto en cada descarga, así que dos descargas simultáneas chocaban.
    /// </summary>
    public void StartListener()
    {
        var mode = (_local.RetrieveMode ?? "move").Trim().ToLowerInvariant();
        if (mode == "get" || _scp != null) return;
        try
        {
            _scp = DicomServerFactory.Create<StoreScp>(_local.MovePort);
            _logger.LogInformation("Receptor C-STORE escuchando en :{Port} AE={Ae}", _local.MovePort, _local.AeTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo abrir el puerto {Port} para C-MOVE (¿otra instancia o puerto ocupado?)", _local.MovePort);
        }
    }

    public void Dispose()
    {
        _scp?.Dispose();
        _scp = null;
        foreach (var g in _nodeGates.Values) g.Dispose();
    }

    private SemaphoreSlim GateFor(PacsNode node) =>
        _nodeGates.GetOrAdd($"{node.Host}:{node.Port}", _ => new SemaphoreSlim(_local.MaxParallelPerNode));

    private void Register(DownloadSession s)
    {
        foreach (var uid in s.StudyUids)
            _waiting.GetOrAdd(uid, _ => new ConcurrentDictionary<string, DownloadSession>())[s.Id] = s;
    }

    private void Unregister(DownloadSession s)
    {
        foreach (var uid in s.StudyUids)
        {
            if (!_waiting.TryGetValue(uid, out var dict)) continue;
            dict.TryRemove(s.Id, out _);
            if (dict.IsEmpty) _waiting.TryRemove(uid, out _);
        }
    }


    public string OutputRoot => _outputRoot;
    public IReadOnlyList<PacsNode> Nodes => _nodes;
    public LocalOptions Local => _local;

    private List<PacsNode> ResolveNodes(IEnumerable<string>? aeTitles)
    {
        var wanted = (aeTitles ?? Enumerable.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wanted.Count == 0)
            return _nodes.ToList();

        var resolved = new List<PacsNode>();
        foreach (var w in wanted)
        {
            var node = _nodes.FirstOrDefault(n =>
                string.Equals(n.AeTitle, w, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Name, w, StringComparison.OrdinalIgnoreCase));
            if (node != null && !resolved.Contains(node))
                resolved.Add(node);
        }
        return resolved;
    }

    private PacsNode ResolveSingleNode(string? aeTitle)
    {
        if (!string.IsNullOrWhiteSpace(aeTitle))
        {
            var node = _nodes.FirstOrDefault(n =>
                string.Equals(n.AeTitle, aeTitle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Name, aeTitle, StringComparison.OrdinalIgnoreCase));
            if (node != null) return node;
        }
        if (_nodes.Count == 0)
            throw new InvalidOperationException("No hay ningún nodo PACS configurado (PacsNodes).");
        return _nodes[0];
    }

    /// <summary>
    /// true cuando no llegó NINGÚN criterio de búsqueda. Sirve para rechazar
    /// consultas que volcarían el PACS entero (miles de estudios) sin filtro.
    /// </summary>
    public static bool SinCriterio(SearchRequest c) =>
        string.IsNullOrWhiteSpace(c.PatientId) &&
        string.IsNullOrWhiteSpace(c.PatientName) &&
        string.IsNullOrWhiteSpace(c.StudyDate) &&
        string.IsNullOrWhiteSpace(c.AccessionNumber) &&
        string.IsNullOrWhiteSpace(c.ModalitiesInStudy) &&
        string.IsNullOrWhiteSpace(c.StudyDescription);

    /// <summary>
    /// Normaliza un identificador (PatientID / AccessionNumber) para C-FIND:
    /// quita espacios, pasa a mayúsculas y, si el usuario escribió un valor parcial
    /// sin comodines, añade '*' al final para que coincida sin tener que teclearlo.
    /// Reglas: vacío → "" (universal); con '*' o '?' → se respeta; entre "comillas" → exacto.
    /// </summary>
    internal static string NormalizarId(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";
        var v = valor.Trim().Replace(" ", "");
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
            return v[1..^1].Trim().ToUpperInvariant();   // búsqueda exacta pedida a propósito
        if (v.IndexOf('*') < 0 && v.IndexOf('?') < 0)
            v += "*";                                     // parcial → comodín al final
        return v.ToUpperInvariant();
    }

    /// <summary>
    /// Como <see cref="NormalizarId"/> pero para texto libre (nombre, descripción):
    /// conserva espacios y, opcionalmente, las mayúsculas originales.
    /// </summary>
    internal static string NormalizarTexto(string? valor, bool mayusculas)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";
        var v = valor.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
            v = v[1..^1].Trim();                          // exacto
        else if (v.IndexOf('*') < 0 && v.IndexOf('?') < 0)
            v += "*";                                     // parcial → comodín al final
        return mayusculas ? v.ToUpperInvariant() : v;
    }

    public async Task<List<StudyInfo>> SearchAsync(SearchRequest criteria, CancellationToken ct = default)
        => await SearchAsync(criteria, ct, null);

    /// <param name="errores">
    /// Frases cortas con lo que impidió consultar cada nodo. Sin esto, un PACS caído, un AE Title
    /// rechazado o un fallo del propio programa se ven en pantalla exactamente igual que «hoy no hay
    /// estudios»: la tabla vacía y el registro en verde. Quien llame aquí decide si eso es un error.
    /// </param>
    public async Task<List<StudyInfo>> SearchAsync(
        SearchRequest criteria, CancellationToken ct, IProducerConsumerCollection<string>? errores)
    {
        if (SinCriterio(criteria))
        {
            _logger.LogWarning("C-FIND rechazado: la consulta no trae ningún criterio");
            return new List<StudyInfo>();
        }

        var targets = ResolveNodes(criteria.AeTitles);
        if (targets.Count == 0)
        {
            _logger.LogWarning("C-FIND: no hay nodos PACS que coincidan con los AE Titles solicitados");
            return new List<StudyInfo>();
        }

        var results = Ordenar(await BuscarEnNodos(targets, criteria, ct, errores));
        if (results.Count > 0)
        {
            _logger.LogInformation("C-FIND: {Count} estudio(s) en {Nodes} nodo(s)", results.Count, targets.Count);
            return results;
        }

        // Ningún nodo respondió: no tiene sentido reintentar con otras ortografías, y decir «0
        // estudios» taparía el motivo. Se devuelve el vacío CON los errores para que quien llamó
        // decida (el API los traduce en un 502; ver StudiesController).
        if (errores is { Count: > 0 })
        {
            _logger.LogWarning("C-FIND: sin resultados y {N} nodo(s) con fallo", errores.Count);
            return results;
        }

        // El PACS compara literal: si él guarda "NUÑEZ" y se tecleó "NUNEZ" (o al revés) no encuentra
        // nada. Se reintentan variantes tolerantes a acentos SOLO cuando la consulta estricta quedó
        // vacía, y cada variante se comprueba aquí en local para no devolver falsos positivos.
        foreach (var (variante, campo, termino) in VariantesTolerantes(criteria))
        {
            ct.ThrowIfCancellationRequested();
            var intento = await BuscarEnNodos(targets, variante, ct, null);
            var coinciden = FiltrarPorCampo(intento, campo, termino);
            if (coinciden.Count == 0) continue;

            _logger.LogInformation(
                "C-FIND tolerante a acentos: {Count} estudio(s) con {Campo}≈{Termino} (la escritura estricta no devolvió nada)",
                coinciden.Count, campo, termino);
            return Ordenar(coinciden);
        }

        _logger.LogInformation("C-FIND: 0 estudios en {Nodes} nodo(s)", targets.Count);
        return results;
    }

    private async Task<List<StudyInfo>> BuscarEnNodos(
        List<PacsNode> targets, SearchRequest criteria, CancellationToken ct,
        IProducerConsumerCollection<string>? errores)
    {
        var tasks = targets.Select(node => SearchOnNodeAsync(node, criteria, ct, errores)).ToList();
        var perNodeResults = await Task.WhenAll(tasks);

        // Quita duplicados (mismo estudio devuelto dos veces por el mismo nodo)
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<StudyInfo>();
        foreach (var r in perNodeResults.SelectMany(x => x))
        {
            if (string.IsNullOrEmpty(r.StudyInstanceUid)) continue;
            var clave = r.SourceAeTitle + "|" + r.StudyInstanceUid;
            if (vistos.Add(clave)) results.Add(r);
        }
        return results;
    }

    /// <summary>Ordena por fecha (recientes primero) y nombre para que la tabla sea útil.</summary>
    private static List<StudyInfo> Ordenar(List<StudyInfo> results) => results
        .OrderByDescending(r => r.StudyDate, StringComparer.Ordinal)
        .ThenBy(r => r.PatientName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// Variantes de <paramref name="criteria"/> para encontrar un nombre o una descripción escritos
    /// con otra ortografía de acentos. Ver <see cref="CriteriosTolerantes"/>.
    /// </summary>
    internal static IEnumerable<(SearchRequest Criterio, string Campo, string Termino)> VariantesTolerantes(
        SearchRequest criteria) => CriteriosTolerantes.Variantes(criteria);

    /// <summary>Comodines candidatos para un solo valor de texto libre.</summary>
    internal static List<string> VariantesAcentos(string? valor) => CriteriosTolerantes.VariantesDe(valor);

    /// <summary>
    /// Se queda con los estudios cuyo campo coincide de verdad con lo tecleado, sin distinguir
    /// acentos ni mayúsculas. Evita que un comodín `?` demasiado abierto llene la tabla de ajenos.
    /// </summary>
    internal static List<StudyInfo> FiltrarPorCampo(List<StudyInfo> estudios, string campo, string termino) =>
        CriteriosTolerantes.Filtrar(estudios, campo, termino);

    private async Task<List<StudyInfo>> SearchOnNodeAsync(
        PacsNode node, SearchRequest criteria, CancellationToken ct, IProducerConsumerCollection<string>? errores)
    {
        var results = new List<StudyInfo>();

        // Normaliza los criterios una sola vez: IDs en mayúsculas y con comodín
        // automático, texto libre con comodín si el usuario no lo escribió.
        var patientId = NormalizarId(criteria.PatientId);
        var accession = NormalizarId(criteria.AccessionNumber);
        var patientName = NormalizarTexto(criteria.PatientName, mayusculas: true);
        var studyDesc = NormalizarTexto(criteria.StudyDescription, mayusculas: false);
        var studyDate = (criteria.StudyDate ?? "").Trim();
        var modality = (criteria.ModalitiesInStudy ?? "").Trim().ToUpperInvariant();

        try
        {
            var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
            request.Dataset.AddOrUpdate(DicomTag.PatientID, patientId);
            request.Dataset.AddOrUpdate(DicomTag.PatientName, patientName);
            request.Dataset.AddOrUpdate(DicomTag.StudyDate, studyDate);
            request.Dataset.AddOrUpdate(DicomTag.AccessionNumber, accession);
            request.Dataset.AddOrUpdate(DicomTag.ModalitiesInStudy, modality);
            request.Dataset.AddOrUpdate(DicomTag.StudyDescription, studyDesc);
            request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "");
            request.Dataset.AddOrUpdate(DicomTag.NumberOfStudyRelatedInstances, "");

            request.OnResponseReceived += (req, response) =>
            {
                if (response.Status == DicomStatus.Pending && response.HasDataset)
                {
                    var ds = response.Dataset;
                    var uid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
                    if (string.IsNullOrEmpty(uid)) return;
                    lock (results)
                    {
                        results.Add(new StudyInfo
                        {
                            PatientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, ""),
                            PatientId = ds.GetSingleValueOrDefault(DicomTag.PatientID, ""),
                            StudyDate = ds.GetSingleValueOrDefault(DicomTag.StudyDate, ""),
                            StudyDescription = ds.GetSingleValueOrDefault(DicomTag.StudyDescription, ""),
                            ModalitiesInStudy = ds.GetSingleValueOrDefault(DicomTag.ModalitiesInStudy, ""),
                            NumberOfInstances = ds.GetSingleValueOrDefault(DicomTag.NumberOfStudyRelatedInstances, ""),
                            StudyInstanceUid = uid,
                            SourceAeTitle = node.AeTitle,
                            SourceName = node.Name,
                        });
                    }
                }
            };

            var client = CreateClient(node);
            await client.AddRequestAsync(request);
            await client.SendAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;                                          // lo pidió el usuario; no es un fallo del PACS
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en C-FIND contra nodo {Node} ({Ae})", node.Name, node.AeTitle);
            // Sin este apunte, la pantalla solo vería «0 estudios» y nadie sabría que el nodo no respondió.
            errores?.TryAdd($"{node.Name}: {MotivoCorto(ex)}");
        }
        return results;
    }

    /// <summary>
    /// Una línea legible del fallo. Las excepciones de .NET traen torres de llamadas y varias líneas;
    /// aquí solo interesa el tipo y el dato (el archivo o el host que no se pudo alcanzar).
    /// </summary>
    internal static string MotivoCorto(Exception ex)
    {
        var primera = (ex.Message ?? "").Split('\n')[0].Trim();
        if (ex is System.IO.FileNotFoundException fn)
            return $"no se pudo cargar {fn.FileName ?? primera}";
        if (ex is System.Net.Sockets.SocketException se)
            return $"red ({se.SocketErrorCode})";
        return string.IsNullOrWhiteSpace(primera) ? ex.GetType().Name : $"{ex.GetType().Name}: {primera}";
    }

    /// <param name="zip">Generar ZIP al terminar (falso en los reenvíos, que solo usan las imágenes de paso).</param>
    /// <param name="subRoot">Subcarpeta propia; así un reenvío puede borrar la suya sin tocar otras descargas.</param>
    /// <param name="verificar">
    /// Comprobar que llegó todo y re-pedir lo que falte. Va encendida para las descargas que ve la
    /// gente; los reenvíos la apagan porque llaman a esto estudio por estudio y ahí los 2 s de
    /// asientado + el C-FIND de conteo multiplicarían el tiempo de un envío grande.
    /// </param>
    public async Task<DownloadResult> DownloadAsync(
        IEnumerable<DownloadItem> items,
        CancellationToken ct = default,
        DownloadProgressSink? sink = null,
        bool zip = true,
        string? subRoot = null,
        bool verificar = true)
    {
        var itemList = items
            .Where(i => !string.IsNullOrWhiteSpace(i.StudyInstanceUid))
            .GroupBy(i => i.StudyInstanceUid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var result = new DownloadResult
        {
            StudiesRequested = itemList.Count,
            OutputPath = _outputRoot
        };

        if (itemList.Count == 0)
        {
            const string none = "No se indicaron StudyInstanceUIDs.";
            result.Messages.Add(none);
            sink?.OnMessage?.Invoke(none);
            return result;
        }

        // Todo el estado de esta descarga vive en la sesión: es seguro llamar esto desde varias PC a la vez.
        var session = new DownloadSession(result, sink, itemList.Count >= 200,
            itemList.Select(i => i.StudyInstanceUid))
        {
            SubRoot = subRoot
        };
        Register(session);

        try
        {
            CleanupOldZips();

            var mode = (_local.RetrieveMode ?? "move").Trim().ToLowerInvariant();
            session.Milestone($"Modo: {mode} · {itemList.Count} estudio(s) · lote={_local.BatchSize}");
            if (session.BulkQuiet)
                session.Milestone("Modo masivo: logs reducidos (solo hitos y errores).");

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var byNode = itemList
                .GroupBy(i => ResolveSingleNode(i.AeTitle))
                .ToList();

            if (byNode.Count > 1)
                session.AddMsg($"Descargando en paralelo de {byNode.Count} nodo(s) PACS: " +
                               string.Join(", ", byNode.Select(g => g.Key.Name)));

            // Los nodos son independientes; con el receptor único ya no hace falta ir uno por uno en C-MOVE.
            await Task.WhenAll(byNode.Select(group =>
                RunNodeAsync(mode, group.Key, group.Select(i => i.StudyInstanceUid).ToList(), session, ct)));

            sw.Stop();
            result.StudiesCompleted = Math.Min(session.StudiesCompleted, itemList.Count);
            result.ImagesSaved = session.ImageCount;

            // ---------- Comprobación de integridad ----------
            // El PACS responde "Success" aunque una imagen se pierda en el camino, así que el éxito de
            // la transferencia se mide contando lo que realmente quedó guardado.
            var hayVerificacion = verificar && _local.VerificarDescargas && !ct.IsCancellationRequested;
            if (hayVerificacion || zip) await EsperarAsentamiento(session, ct);

            if (hayVerificacion)
            {
                sink?.OnPhase?.Invoke("verifying");
                var esperado = await ContarLoQueDebeLlegarAsync(itemList, session, ct);
                var faltantes = HallarFaltantes(itemList, esperado, session, result);
                result.Verified = true;
                result.MissingInstancesInitially = result.MissingInstances;

                var ronda = 0;
                while (faltantes.Count > 0 && ronda < _local.ReintentosMaximos && !ct.IsCancellationRequested)
                {
                    ronda++;
                    result.VerificationRounds = ronda;
                    var faltanImagenes = faltantes.Sum(f => f.Faltantes);
                    session.Milestone(
                        $"Verificación: faltan {faltanImagenes} imagen(es) en {faltantes.Count} estudio(s). " +
                        $"Reintento {ronda}/{_local.ReintentosMaximos}…");
                    sink?.OnPhase?.Invoke("retrying");

                    var repeticiones = itemList
                        .Where(i => faltantes.Any(f =>
                            string.Equals(f.StudyInstanceUid, i.StudyInstanceUid, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    result.RetriedStudies += repeticiones.Count;

                    // C-MOVE/C-GET a nivel de estudio vuelve a mandar TODAS sus imágenes; los archivos se
                    // guardan con el SOP InstanceUID como nombre, así que las repetidas se sobrescriben
                    // idénticas y solo aportan las que faltaban.
                    await Task.WhenAll(repeticiones
                        .GroupBy(i => ResolveSingleNode(i.AeTitle))
                        .Select(g => RunNodeAsync(mode, g.Key, g.Select(i => i.StudyInstanceUid).ToList(), session, ct)));

                    await EsperarAsentamiento(session, ct);
                    faltantes = HallarFaltantes(itemList, esperado, session, result);
                }

                if (faltantes.Count == 0)
                {
                    session.Milestone(result.MissingInstancesInitially > 0
                        ? $"Verificación OK: {result.MissingInstancesInitially} imagen(es) que faltaban ya llegaron."
                        : $"Verificación OK: llegaron las {result.ImagesSaved} imagen(es) esperadas.");
                }
                else
                {
                    foreach (var f in faltantes.Take(20))
                        session.AddMsg(
                            $"⚠ Incompleto: {NombreEstudio(f)} · {f.Received}/{(f.Expected > 0 ? f.Expected.ToString() : "?")} imágenes" +
                            (string.IsNullOrEmpty(f.SourceName) ? "" : $" · nodo {f.SourceName}"),
                            important: true);
                    if (faltantes.Count > 20)
                        session.AddMsg($"… y {faltantes.Count - 20} estudio(s) más incompleto(s).", important: true);
                    session.AddMsg(
                        $"Aviso: se siguen echando de menos {result.MissingInstances} imagen(es) tras " +
                        $"{ronda} reintento(s). Vuelve a descargar estos estudios más tarde o avisa a sistemas.",
                        important: true);
                }
            }

            result.ImagesSaved = session.UniqueImagesTotal();
            result.StudiesCompleted = Math.Min(
                Math.Max(result.StudiesCompleted, itemList.Count(i => session.UniqueImagesForStudy(i.StudyInstanceUid) > 0)),
                itemList.Count);

            // ---------- ZIP, en partes si la descarga es grande ----------
            if (!zip)
            {
                session.Milestone("Sin ZIP: estas imágenes son paso intermedio de un reenvío.");
            }
            else
            {
                try
                {
                    sink?.OnZipping?.Invoke();
                    sink?.OnPhase?.Invoke("zipping");
                    session.Milestone("Empaquetando ZIP…");
                    var partes = await Task.Run(() => CrearPartesDeZip(session), ct);
                    result.ZipParts = partes;
                    if (partes.Count > 0)
                    {
                        result.ZipFileName = partes[0].FileName;
                        result.ZipDownloadUrl = partes[0].DownloadUrl;
                        var mb = partes.Sum(p => p.Bytes) / (1024.0 * 1024.0);
                        session.Milestone(
                            partes.Count == 1
                                ? $"ZIP listo para tu PC: {partes[0].FileName} ({partes[0].Bytes / (1024.0 * 1024.0):F1} MB)"
                                : $"ZIP en {partes.Count} partes · {mb:F0} MB en total: {partes[0].FileName} …");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creando ZIP");
                    session.Milestone($"Aviso: no se pudo crear ZIP ({ex.Message}). Archivos en servidor: {_outputRoot}");
                }
            }

            session.Milestone(
                $"Fin en {sw.Elapsed.TotalSeconds:F1}s · {result.StudiesCompleted}/{result.StudiesRequested} estudios · " +
                $"{result.ImagesSaved} imágenes");
            return result;
        }
        finally
        {
            Unregister(session);
        }
    }

    /// <summary>
    /// Espera a que dejen de llegar imágenes. Un C-MOVE puede cerrar su asociación y el PACS seguir
    /// empujando las últimas imágenes por otra conexión; si se empaqueta o se verifica justo en ese
    /// instante, imágenes que sí llegaron quedarían fuera del ZIP o contarían como faltantes.
    /// Sale cuando el contador lleva 2 s sin moverse (o a los 30 s como máximo).
    /// </summary>
    private static async Task EsperarAsentamiento(DownloadSession session, CancellationToken ct)
    {
        var estables = 0;
        var ultimo = session.ImageCount;
        for (var ronda = 0; ronda < 60 && estables < 4; ronda++)
        {
            if (ct.IsCancellationRequested) return;
            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { return; }
            var ahora = session.ImageCount;
            if (ahora == ultimo) estables++;
            else { estables = 0; ultimo = ahora; }
        }
    }

    /// <summary>
    /// Cuántas imágenes debería tener cada estudio descargado. Se toma del C-FIND de la búsqueda
    /// (<see cref="DownloadItem.ExpectedInstances"/>); cuando ese dato no vino, se le pregunta al
    /// PACS con un C-FIND de estudio —hasta un tope, para no machacar el nodo en descargas masivas—.
    /// </summary>
    private async Task<ConcurrentDictionary<string, int>> ContarLoQueDebeLlegarAsync(
        List<DownloadItem> items, DownloadSession session, CancellationToken ct)
    {
        var esperado = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
            esperado[i.StudyInstanceUid] = i.ExpectedInstances ?? 0;

        if (!_local.VerificarConPacs) return esperado;

        var sinDato = items
            .Where(i => (i.ExpectedInstances ?? 0) <= 0)
            .ToList();
        if (sinDato.Count == 0) return esperado;

        var limite = _local.VerificarConPacsMaxEstudios;
        var aPreguntar = limite > 0 ? sinDato.Take(limite).ToList() : sinDato;
        if (aPreguntar.Count < sinDato.Count)
            session.Milestone(
                $"El PACS no informó el conteo de {sinDato.Count} estudio(s); se verifican con consulta " +
                $"los primeros {aPreguntar.Count} (tope {limite}).");

        session.Milestone($"Consultando al PACS el número de imágenes de {aPreguntar.Count} estudio(s)…");
        var pendientes = aPreguntar
            .GroupBy(i => ResolveSingleNode(i.AeTitle))
            .SelectMany(g => g.Select(i => ConsultaConteo(i, g.Key, esperado, session, ct)))
            .ToList();
        await Task.WhenAll(pendientes);
        return esperado;
    }

    private async Task ConsultaConteo(
        DownloadItem item, PacsNode node, ConcurrentDictionary<string, int> esperado,
        DownloadSession session, CancellationToken ct)
    {
        var gate = GateFor(node);
        int? total = null;
        try
        {
            await gate.WaitAsync(ct);
            try { total = await ContarInstanciasEnPacsAsync(node, item.StudyInstanceUid, ct); }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo contar instancias de {Uid} en {Node}", item.StudyInstanceUid, node.Name);
        }

        if (total is > 0) esperado[item.StudyInstanceUid] = total.Value;
        else session.AddMsg($"[{node.Name}] El PACS no devolvió el conteo de {item.StudyInstanceUid}; " +
                            "se comprobará solo que haya llegado algo.");
    }

    /// <summary>C-FIND de estudio para leer NumberOfStudyRelatedInstances de un UID concreto.</summary>
    private async Task<int?> ContarInstanciasEnPacsAsync(PacsNode node, string studyUid, CancellationToken ct)
    {
        var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
        request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyUid);
        request.Dataset.AddOrUpdate(DicomTag.NumberOfStudyRelatedInstances, "");

        var hallado = 0;
        request.OnResponseReceived += (req, response) =>
        {
            if (response.Status != DicomStatus.Pending || !response.HasDataset) return;
            var bruto = response.Dataset.GetSingleValueOrDefault(DicomTag.NumberOfStudyRelatedInstances, "");
            if (int.TryParse(bruto.Trim(), out var n) && n > hallado) hallado = n;
        };

        var client = CreateClient(node);
        await client.AddRequestAsync(request);
        await client.SendAsync(ct);
        return hallado > 0 ? hallado : null;
    }

    /// <summary>
    /// Compara lo que llegó contra lo que debía llegar. El criterio vive en <see cref="IntegridadDescarga"/>
    /// (lógica pura, comprobable sin red); aquí solo se le dan las dos funciones de conteo y se anota el
    /// resultado en <paramref name="result"/>.
    /// </summary>
    private static List<IncompleteStudy> HallarFaltantes(
        List<DownloadItem> items, ConcurrentDictionary<string, int> esperado,
        DownloadSession session, DownloadResult result)
    {
        var faltantes = IntegridadDescarga.Faltantes(
            items,
            uid => esperado.TryGetValue(uid, out var meta) ? meta : 0,
            uid => session.UniqueImagesForStudy(uid));

        result.IncompleteStudies = faltantes;
        result.MissingInstances = faltantes.Sum(f => f.Faltantes);
        return faltantes;
    }

    private static string NombreEstudio(IncompleteStudy f)
    {
        var nombre = (f.PatientName ?? "").Trim();
        var partes = new List<string>();
        if (nombre.Length > 0) partes.Add(nombre);
        if (!string.IsNullOrWhiteSpace(f.StudyDate)) partes.Add(f.StudyDate);
        if (partes.Count == 0) partes.Add(f.StudyInstanceUid);
        return string.Join(" · ", partes);
    }

    private async Task RunNodeAsync(string mode, PacsNode node, List<string> uidList, DownloadSession session, CancellationToken ct)
    {
        if (mode == "get")
        {
            await DownloadGetAsync(node, uidList, session, ct);
        }
        else if (mode == "auto")
        {
            await DownloadGetAsync(node, uidList, session, ct);
            // Si el C-GET no trajo ninguna imagen de este nodo, se reintenta por C-MOVE.
            if (session.ImagesForStudies(uidList) == 0)
            {
                session.AddMsg($"[{node.Name}] C-GET sin imágenes → C-MOVE…");
                await DownloadMoveAsync(node, uidList, session, ct);
            }
        }
        else
        {
            await DownloadMoveAsync(node, uidList, session, ct);
        }
    }

    /// <summary>
    /// Empaqueta en ZIP las carpetas de ESTA sesión, repartidas en **partes numeradas** cuando la
    /// descarga es grande (más de <c>ZipMaxStudies</c> estudios o más de <c>ZipMaxMBPorParte</c> MB
    /// por parte). Antes, al pasar de ese umbral, directamente no se generaba ZIP y la persona se
    /// quedaba sin sus archivos; ahora se bajan varias partes seguidas y no se pierde nada.
    ///
    /// Sin compresión: los .dcm ya suelen ir comprimidos (JPEG/J2K/RLE); Deflate solo gasta CPU y
    /// ralentiza el empaquetado sin reducir casi nada el tamaño. Un estudio nunca se parte entre dos
    /// ZIP: la unidad de reparto es la carpeta del estudio.
    /// </summary>
    private List<ZipPart> CrearPartesDeZip(DownloadSession session)
    {
        var partes = new List<ZipPart>();
        var folders = session.CreatedDirs;
        if (session.ImageCount <= 0 && folders.Count == 0) return partes;

        // 1) Archivos de cada carpeta de estudio, con su peso (sirve para decidir dónde cortar).
        var carpetas = new List<List<(string Ruta, string Entrada, long Bytes)>>();
        foreach (var rel in folders.OrderBy(x => x, StringComparer.Ordinal))
        {
            var dir = Path.Combine(_outputRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;
            var archivos = new List<(string Ruta, string Entrada, long Bytes)>();
            foreach (var file in Directory.EnumerateFiles(dir, "*.dcm", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                long bytes = 0;
                try { bytes = new FileInfo(file).Length; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                var entrada = Path.GetRelativePath(_outputRoot, file).Replace('\\', '/');
                archivos.Add((file, entrada, bytes));
            }
            if (archivos.Count > 0) carpetas.Add(archivos);
        }
        if (carpetas.Count == 0) return partes;

        // 2) Repartir las carpetas en partes. El reparto es lógica pura (IntegridadDescarga), así se
        //    puede comprobar sola, sin red ni disco. El corte se decide con lo que lleva LA PARTE
        //    actual y no con el acumulado: así cada ZIP cumple el tope por separado.
        var metadatos = new List<IntegridadDescarga.CarpetaDeZip>(carpetas.Count);
        var porClave = new Dictionary<string, List<(string Ruta, string Entrada, long Bytes)>>(StringComparer.Ordinal);
        for (var i = 0; i < carpetas.Count; i++)
        {
            // La clave es el índice: nunca choca, aunque dos carpetas de estudio compartan nombre.
            var clave = i.ToString(CultureInfo.InvariantCulture);
            metadatos.Add(new IntegridadDescarga.CarpetaDeZip(clave, carpetas[i].Count, carpetas[i].Sum(a => a.Bytes)));
            porClave[clave] = carpetas[i];
        }

        var grupos = IntegridadDescarga
            .DividirEnPartes(metadatos, _local.ZipMaxStudies, (long)_local.ZipMaxMBPorParte * 1048576L)
            .Select(parte => parte.Select(c => porClave[c.Clave]).ToList())
            .ToList();

        // 3) Escribir cada parte.
        var zipsDir = Path.Combine(_outputRoot, "_zips");
        Directory.CreateDirectory(zipsDir);
        var sello = $"{DateTime.Now:yyyyMMdd_HHmmss}_{session.Id[..8]}";
        var totalPartes = grupos.Count;

        for (var i = 0; i < totalPartes; i++)
        {
            var archivos = grupos[i].SelectMany(c => c).ToList();
            var zipName = totalPartes == 1
                ? $"dicom_{sello}.zip"
                : $"dicom_{sello}_parte{i + 1}de{totalPartes}.zip";
            var zipPath = Path.Combine(zipsDir, zipName);
            EscribirZip(zipPath, archivos);

            var fi = new FileInfo(zipPath);
            if (!fi.Exists || fi.Length == 0) continue;

            partes.Add(new ZipPart
            {
                Number = i + 1,
                Total = totalPartes,
                FileName = zipName,
                DownloadUrl = "/api/dicom/files/" + zipName,
                Bytes = fi.Length,
                Studies = grupos[i].Count,
                Files = archivos.Count,
            });
            session.AddMsg(
                totalPartes == 1
                    ? $"ZIP {fi.Length / (1024.0 * 1024.0):F1} MB · {archivos.Count} archivos (sin compresión, listo para bajar)"
                    : $"Parte {i + 1}/{totalPartes}: {zipName} · {fi.Length / (1024.0 * 1024.0):F1} MB · {archivos.Count} archivos",
                important: true);
        }
        return partes;
    }

    /// <summary>
    /// Escribe un ZIP sin comprimir. FileStream con buffer de 1 MB + lectura secuencial: así el
    /// empaquetado va a la velocidad del disco en lugar de quedarse en el CPU.
    /// </summary>
    private static void EscribirZip(string zipPath, List<(string Ruta, string Entrada, long Bytes)> archivos)
    {
        using var fs = new FileStream(
            zipPath, FileMode.Create, FileAccess.Write, FileShare.None,
            1 * 1024 * 1024, FileOptions.SequentialScan);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        var buf = new byte[256 * 1024];
        foreach (var (ruta, entryName, _) in archivos)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            using var src = new FileStream(
                ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                256 * 1024, FileOptions.SequentialScan);
            int read;
            while ((read = src.Read(buf, 0, buf.Length)) > 0)
                entryStream.Write(buf, 0, read);
        }
    }

    /// <summary>Borra ZIP viejos (con varias PC descargando se acumulan rápido).</summary>
    private void CleanupOldZips()
    {
        if (_local.ZipRetentionHours <= 0) return;
        try
        {
            var zipsDir = Path.Combine(_outputRoot, "_zips");
            if (!Directory.Exists(zipsDir)) return;
            var limit = DateTime.UtcNow.AddHours(-_local.ZipRetentionHours);
            foreach (var f in Directory.EnumerateFiles(zipsDir, "*.zip"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(f) < limit) File.Delete(f);
                }
                catch (IOException) { /* en uso: se borra en una limpieza posterior */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudieron limpiar ZIP antiguos");
        }
    }

    /// <summary>
    /// C-GET por lotes. Dentro de cada lote hay varias asociaciones en paralelo, pero el tope
    /// (MaxParallelPerNode) es global por PACS: se reparte entre todas las PC que descargan.
    /// </summary>
    private async Task DownloadGetAsync(PacsNode node, List<string> uidList, DownloadSession session, CancellationToken ct)
    {
        var gate = GateFor(node);
        var batchSize = _local.BatchSize;
        var total = uidList.Count;
        var processed = 0;
        var totalOk = 0;

        for (var offset = 0; offset < total; offset += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = uidList.GetRange(offset, Math.Min(batchSize, total - offset));
            var batchNo = offset / batchSize + 1;
            var batchTotal = (total + batchSize - 1) / batchSize;

            if (total > batchSize)
                session.Milestone($"[{node.Name}] C-GET lote {batchNo}/{batchTotal} ({batch.Count} estudios)…");

            var okCount = 0;

            var tasks = batch.Select(async studyUid =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    session.AddMsg($"[{node.Name}] C-GET → {studyUid}");
                    try
                    {
                        var client = CreateClient(node);
                        var pcs = DicomPresentationContext.GetScpRolePresentationContextsFromStorageUids(
                            DicomStorageCategory.Image,
                            DicomTransferSyntax.ExplicitVRLittleEndian,
                            DicomTransferSyntax.ImplicitVRLittleEndian);
                        client.AdditionalPresentationContexts.AddRange(pcs);

                        client.OnCStoreRequest += async req =>
                        {
                            try
                            {
                                await StoreIncomingAsync(req.Dataset, session);
                                return new DicomCStoreResponse(req, DicomStatus.Success);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error guardando (C-GET)");
                                return new DicomCStoreResponse(req, DicomStatus.ProcessingFailure);
                            }
                        };

                        var getRequest = new DicomCGetRequest(studyUid);
                        getRequest.OnResponseReceived += (req, response) =>
                        {
                            if (response.Status.State == DicomState.Success)
                            {
                                Interlocked.Increment(ref okCount);
                                session.CompleteStudy();
                                session.AddMsg($"[{node.Name}] C-GET OK: {studyUid}");
                            }
                            else if (response.Status.State == DicomState.Failure)
                                session.AddMsg($"[{node.Name}] C-GET fallo {studyUid}: {response.Status}");
                        };

                        await client.AddRequestAsync(getRequest);
                        await client.SendAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        session.AddMsg($"[{node.Name}] Excepción C-GET ({studyUid}): {ex.Message}");
                    }
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
            totalOk += okCount;
            processed += batch.Count;

            if (total > batchSize || session.BulkQuiet)
                session.Milestone(
                    $"[{node.Name}] C-GET avance: {processed}/{total} estudios · {session.ImageCount} imgs · ok={totalOk}");
        }
    }

    /// <summary>
    /// C-MOVE por lotes. Las imágenes llegan al receptor C-STORE único (<see cref="StartListener"/>),
    /// que las reparte a la sesión —y por tanto a la barra de progreso— que pidió cada estudio.
    /// </summary>
    private async Task DownloadMoveAsync(PacsNode node, List<string> uidList, DownloadSession session, CancellationToken ct)
    {
        if (_scp == null)
        {
            session.Milestone($"[{node.Name}] Error C-MOVE: el receptor local :{_local.MovePort} no está activo (puerto ocupado). Revisa el log del servicio.");
            return;
        }

        var gate = GateFor(node);
        try
        {
            session.Milestone($"[{node.Name}] Receptor local :{_local.MovePort} AE={_local.AeTitle}");

            var batchSize = _local.BatchSize;
            var total = uidList.Count;
            var processed = 0;

            for (var offset = 0; offset < total; offset += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = uidList.GetRange(offset, Math.Min(batchSize, total - offset));
                var batchNo = offset / batchSize + 1;
                var batchTotal = (total + batchSize - 1) / batchSize;

                if (total > batchSize)
                    session.Milestone($"[{node.Name}] C-MOVE lote {batchNo}/{batchTotal} ({batch.Count} estudios)…");

                // Un lote a la vez por turno: con varias PC los lotes se intercalan y todas avanzan.
                await gate.WaitAsync(ct);
                try
                {
                    var client = CreateClient(node);
                    var pendingMoves = batch.Count;
                    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                    foreach (var studyUid in batch)
                    {
                        ct.ThrowIfCancellationRequested();
                        session.AddMsg($"[{node.Name}] C-MOVE → {studyUid}");

                        var moveRequest = new DicomCMoveRequest(_local.AeTitle, studyUid);
                        moveRequest.OnResponseReceived += (req, response) =>
                        {
                            if (response.Status.State == DicomState.Pending)
                                return;

                            if (response.Status.State == DicomState.Success)
                            {
                                session.CompleteStudy();
                                session.AddMsg($"[{node.Name}] C-MOVE OK: {studyUid}");
                            }
                            else if (response.Status.State == DicomState.Failure)
                            {
                                session.AddMsg($"[{node.Name}] C-MOVE fallo {studyUid}: {response.Status}");
                                if (response.Status.Code == 0xC000)
                                {
                                    session.AddMsg(
                                        $"  → Registra en {node.Name}: AE={_local.AeTitle} IP=IP_DEL_SERVIDOR Puerto={_local.MovePort}",
                                        important: true);
                                }
                            }

                            if (Interlocked.Decrement(ref pendingMoves) <= 0)
                                tcs.TrySetResult();
                        };

                        await client.AddRequestAsync(moveRequest);
                    }

                    var sendTask = client.SendAsync(ct);
                    await Task.WhenAny(sendTask, tcs.Task);

                    // Estabilizar C-STORE en vuelo, midiendo solo el contador de ESTA sesión.
                    var stable = 0;
                    var last = session.ImageCount;
                    var maxWaitRounds = session.BulkQuiet ? 15 : 8; // ~3 s o ~1.6 s
                    while (stable < 3 && maxWaitRounds-- > 0)
                    {
                        await Task.Delay(200, ct);
                        var now = session.ImageCount;
                        if (now == last) stable++;
                        else { stable = 0; last = now; }
                    }

                    try { await sendTask; }
                    catch (Exception ex)
                    {
                        session.Milestone($"[{node.Name}] Aviso envío C-MOVE lote {batchNo}: {ex.Message}");
                    }
                }
                finally
                {
                    gate.Release();
                }

                processed += batch.Count;
                session.Milestone(
                    $"[{node.Name}] C-MOVE avance: {processed}/{total} estudios · {session.ImageCount} imgs");
            }

            session.Milestone($"[{node.Name}] C-MOVE fin: {session.ImagesForStudies(uidList)} imagen(es) de este nodo");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error C-MOVE");
            session.Milestone($"[{node.Name}] Error C-MOVE: {ex.Message}");
        }
    }

    /// <summary>Punto de entrada del receptor C-STORE (C-MOVE): reparte por StudyInstanceUID.</summary>
    private async Task OnIncomingDatasetAsync(DicomDataset ds)
    {
        try
        {
            await StoreIncomingAsync(ds, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error guardando imagen recibida por C-MOVE");
            throw;
        }
    }

    /// <summary>
    /// Guarda la imagen UNA vez por carpeta de destino y la acredita a todas las sesiones que esperan
    /// ese estudio (si dos PC piden el mismo estudio, ambas lo reciben, cada una con su barra y su ZIP).
    /// Las sesiones de reenvío escriben en su propia <see cref="DownloadSession.SubRoot"/>, así que
    /// pueden borrarse al terminar sin tocar lo que descargó otra persona.
    /// </summary>
    internal async Task StoreIncomingAsync(DicomDataset ds, DownloadSession? fallback)
    {
        var studyUid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");

        var sessions = new List<DownloadSession>();
        if (!string.IsNullOrEmpty(studyUid) && _waiting.TryGetValue(studyUid, out var dict))
            sessions.AddRange(dict.Values);
        if (fallback != null && !sessions.Contains(fallback))
            sessions.Add(fallback);

        var patientFolder = BuildPatientFolderName(ds);
        var studyFolder = BuildStudyFolderName(ds);
        var sopUid = ds.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, Guid.NewGuid().ToString("N"));

        // Sin sesiones que lo esperen (empujó otro PACS a MEDI, o llegó tarde tras una cancelación)
        // se guarda en la raíz como siempre; el depurador lo retira después.
        var grupos = sessions.Count == 0
            ? new List<(string Sub, List<DownloadSession> Sesiones)> { ("", sessions) }
            : sessions.GroupBy(s => s.SubRoot ?? "")
                      .Select(g => (g.Key, g.ToList()))
                      .ToList();

        foreach (var (subRaw, sesiones) in grupos)
        {
            var sub = subRaw.Length == 0 ? "" : subRaw.Trim('/') + "/";
            var fullKey = $"{sub}{patientFolder}/{studyFolder}";
            var studyPath = Path.Combine(_outputRoot,
                fullKey.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(studyPath); // idempotente y seguro entre hilos

            var filePath = Path.Combine(studyPath, $"{sopUid}.dcm");

            // Temporal único + renombrar: dos descargas del mismo estudio no se corrompen entre sí.
            var tmp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await new DicomFile(ds).SaveAsync(tmp);
                try
                {
                    File.Move(tmp, filePath, overwrite: true);
                }
                catch (IOException) when (File.Exists(filePath))
                {
                    // Otra descarga escribió/lee el mismo archivo (mismo contenido): se descarta el temporal.
                    File.Delete(tmp);
                }
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }

            foreach (var s in sesiones)
            {
                if (s.NoteDir(fullKey))
                {
                    s.AddMsg($"Nueva carpeta: {fullKey}");
                    s.Sink?.OnFolderCreated?.Invoke(fullKey);
                }
                var n = s.IncrementImages(studyUid, sopUid);
                var step = s.BulkQuiet ? 200 : 50;
                if (n == 1 || n % step == 0)
                    s.AddMsg($"Guardadas {n} imagen(es)…", important: true);
            }
        }
    }

    private IDicomClient CreateClient(PacsNode node)
    {
        var client = DicomClientFactory.Create(
            node.Host, node.Port, false, _local.AeTitle, node.AeTitle);
        client.NegotiateAsyncOps(16, 16);
        return client;
    }

    private static string Sanitize(string? value, string fallback, int maxLen = 50)
    {
        value = (value ?? "").Trim();
        if (string.IsNullOrEmpty(value)) return fallback;
        value = value.Replace("^", "_");
        foreach (var ch in Path.GetInvalidFileNameChars())
            value = value.Replace(ch, '_');
        value = value.Trim('_', ' ');
        if (value.Length == 0) return fallback;
        return value.Length > maxLen ? value[..maxLen] : value;
    }

    private static string BuildPatientFolderName(DicomDataset ds)
    {
        var name = Sanitize(ds.GetSingleValueOrDefault(DicomTag.PatientName, ""), "SinNombre");
        var id = Sanitize(ds.GetSingleValueOrDefault(DicomTag.PatientID, ""), "");
        return string.IsNullOrEmpty(id) ? name : $"{name}_{id}";
    }

    private static string BuildStudyFolderName(DicomDataset ds)
    {
        var date = Sanitize(ds.GetSingleValueOrDefault(DicomTag.StudyDate, ""), "SinFecha", 8);
        var modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "");
        if (string.IsNullOrEmpty(modality))
            modality = ds.GetSingleValueOrDefault(DicomTag.ModalitiesInStudy, "");
        modality = Sanitize(modality, "XX", 8);
        if (modality.Contains('\\')) modality = modality.Split('\\')[0];
        var uid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "SINUID");
        var shortUid = uid.Length > 8 ? uid[^8..] : uid;
        return $"{date}_{modality}_{shortUid}";
    }
}

public class StoreScp : DicomService, IDicomServiceProvider, IDicomCStoreProvider
{
    /// <summary>Lo asigna DicomQrService: recibe cada imagen y la reparte a la sesión correcta.</summary>
    public static Func<DicomDataset, Task>? Handler { get; set; }

    public StoreScp(INetworkStream stream, Encoding fallbackEncoding,
        Microsoft.Extensions.Logging.ILogger log, DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, log, dependencies) { }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        foreach (var pc in association.PresentationContexts)
        {
            if (pc.AbstractSyntax == DicomUID.Verification ||
                pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None)
            {
                pc.AcceptTransferSyntaxes(
                    DicomTransferSyntax.ExplicitVRLittleEndian,
                    DicomTransferSyntax.ImplicitVRLittleEndian);
            }
            else
                pc.SetResult(DicomPresentationContextResult.RejectAbstractSyntaxNotSupported);
        }
        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync() => SendAssociationReleaseResponseAsync();
    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }
    public void OnConnectionClosed(Exception? exception) { }

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        try
        {
            var handler = Handler;
            if (handler == null)
                return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);

            await handler(request.Dataset);
            return new DicomCStoreResponse(request, DicomStatus.Success);
        }
        catch
        {
            // El error ya se registra en DicomQrService.OnIncomingDatasetAsync.
            return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
        }
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e) => Task.CompletedTask;
}
