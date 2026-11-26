using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MySqlConnector;

namespace Meditechnosys.Intranet.Patologia;

/// Resultado de importar un PDF desde carpeta (no desde la web).
public record ResultadoImport(
    bool Ok, bool Omitido, bool Reintentar, long? Id, string Archivo, string Area,
    string? Folio, string? Nombre, string? Modalidad, string? Aviso, string? Error, string? MovidoA,
    string? Estudio = null);

/// Resumen de una importación por Excel.
public record FilaExcel(
    string Archivo, string Area, bool Nuevo, long? Id, string? Folio,
    string? Modalidad, string? Aviso, string? Error, bool Corregido,
    string? Estudio = null);

public record ResultadoExcel(int Nuevos, int Omitidos, int Errores, int Corregidos, List<FilaExcel> Filas);

/// Importación desde las carpetas del sistema + importación manual por Excel.
/// Port de app/importador.py y de manage.py::importar_excel.
public partial class Importador
{
    // Nombre de un PDF ya almacenado por el sistema: 00000123.pdf (ID de la BD).
    [GeneratedRegex(@"^\d{8}\.pdf$", RegexOptions.IgnoreCase)]
    private static partial Regex ReAlmacenado();

    private static readonly object Candado = new();

    private static readonly Dictionary<string, string[]> ColsExcel = new()
    {
        ["archivo"] = new[] { "archivo", "ruta", "path", "pdf", "archivopdf", "rutapdf", "nombrearchivo" },
        ["area"] = new[] { "area", "especialidad" },
        ["folio"] = new[] { "folio" },
        ["fecha"] = new[] { "fecha", "fechareporte", "fechadeestudio" },
        ["nombre"] = new[] { "nombre", "paciente", "nombrepaciente", "nombrecompleto" },
        ["nss"] = new[] { "nss", "numsegurosocial", "numerodeseguridadsocial" },
        ["agregado"] = new[] { "agregado", "agregadomedico" },
        ["modalidad"] = new[] { "modalidad", "tipodeestudio", "tipoestudio" },
        ["estudio"] = new[] { "estudio", "designacionestudio" },
    };

    // Orden en que se aplican las correcciones del Excel.
    private static readonly string[] CamposCorreccion = { "folio", "nombre", "nss", "agregado", "modalidad", "estudio" };

    private static readonly Dictionary<string, string> CampoColBd = new()
    {
        ["folio"] = "folio", ["nombre"] = "nombre", ["nss"] = "nss",
        ["agregado"] = "agregado_medico", ["modalidad"] = "modalidad", ["estudio"] = "estudio",
    };

    private readonly PatoDb _db;
    private readonly Repositorio _repo;
    private readonly ILogger _log;

    public Importador(PatoDb db, Repositorio repo, ILogger<Importador> log)
    {
        _db = db; _repo = repo; _log = log;
    }

    // ------------------------------------------------------------------ vigilancia
    public List<ResultadoImport> EscanearInbox(bool mover = true)
    {
        var res = new List<ResultadoImport>();
        foreach (var area in PatoDb.Areas.Keys)
            res.AddRange(EscanearCarpeta(area, null, mover));
        return res;
    }

    public List<ResultadoImport> EscanearCarpeta(string area, string? carpeta = null, bool mover = true)
    {
        _db.CarpetaArea(area); // valida el área
        carpeta ??= _db.InboxArea(area);
        var procesados = _db.ProcesadosArea(area);
        var errores = _db.ErroresArea(area);
        var resultados = new List<ResultadoImport>();
        if (!Directory.Exists(carpeta)) return resultados;

        lock (Candado)
        {
            string[] pdfs;
            try
            {
                pdfs = Directory.GetFiles(carpeta, "*.pdf")
                    .Where(p => Path.GetExtension(p).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception e) { _log.LogDebug("No se pudo listar {Carpeta}: {Msg}", carpeta, e.Message); return resultados; }

            var almacenados = NombresAlmacenados(area);
            foreach (var ruta in pdfs)
            {
                if (!File.Exists(ruta)) continue;
                // Nunca tocar los PDF que el sistema ya guardó en el almacén,
                // aunque la carpeta de entrada y la de almacenamiento sean la misma.
                if (EsAlmacenado(Path.GetFileName(ruta), almacenados)) continue;

                var item = ProcesarArchivo(ruta, area);
                if (mover && !item.Reintentar)
                {
                    var movido = MoverSilencioso(ruta, item.Ok ? procesados : errores);
                    if (movido is not null) item = item with { MovidoA = movido };
                }
                resultados.Add(item);

                if (item.Ok && item.Omitido)
                    _log.LogInformation("[{Area}] Omitido {Archivo} — {Aviso}", area, item.Archivo, item.Aviso);
                else if (item.Ok)
                    _log.LogInformation("[{Area}] Importado {Archivo} → id {Id} · folio {Folio} · {Nombre}",
                        area, item.Archivo, item.Id, item.Folio, item.Nombre);
                else
                    _log.LogWarning("[{Area}] Falló {Archivo}: {Error}", area, item.Archivo, item.Error);
            }
        }
        return resultados;
    }

    public ResultadoImport ProcesarArchivo(string ruta, string area, string etiqueta = "carpeta")
    {
        var nombre = Path.GetFileName(ruta);
        try
        {
            var (contenido, suave) = LeerEstable(ruta);
            if (suave == "omitido_ausente")
                return new ResultadoImport(true, true, false, null, nombre, area, null, null, null, "Archivo ya no estaba en la carpeta", null, null);
            if (contenido is null)
                return new ResultadoImport(true, true, true, null, nombre, area, null, null, null, "Archivo ocupado o incompleto; se reintentará", null, null);
            var r = _repo.Guardar(area, contenido, nombre, etiqueta);
            return new ResultadoImport(r.Ok, r.Omitido, false, r.Id, nombre, area, r.Folio, r.Nombre, r.Modalidad, r.Aviso, r.Error, null, r.Estudio);
        }
        catch (Exception e)
        {
            var msg = e.Message;
            if (msg.Contains("No such file") || msg.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
                return new ResultadoImport(true, true, false, null, nombre, area, null, null, null, "Archivo ya no estaba en la carpeta", null, null);
            return new ResultadoImport(false, false, false, null, nombre, area, null, null, null, null, msg, null);
        }
    }

    /// Espera a que el archivo esté estable (tamaño sin cambios) y lo lee compartiendo
    /// el acceso, para que un antivirus o una copia en curso no provoquen un error.
    private static (byte[]? Contenido, string? Suave) LeerEstable(string ruta, int intentos = 6)
    {
        long tamPrev = -1;
        for (var i = 0; i < intentos; i++)
        {
            try
            {
                if (!File.Exists(ruta)) return (null, "omitido_ausente");
                var tam = new FileInfo(ruta).Length;
                if (tam <= 0 || tam != tamPrev) { tamPrev = tam; Thread.Sleep(500); continue; }
                using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                var data = ms.ToArray();
                if (data.Length > 0) return (data, null);
                Thread.Sleep(500);
            }
            catch (FileNotFoundException) { return (null, "omitido_ausente"); }
            catch (IOException) { Thread.Sleep(500); }
            catch (UnauthorizedAccessException) { Thread.Sleep(500); }
        }
        return (null, "omitido_inestable");
    }

    private HashSet<string> NombresAlmacenados(string area)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cn = _db.Conexion(); cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT ruta_pdf FROM reportes WHERE area = @a AND ruta_pdf <> ''";
            cmd.Parameters.AddWithValue("@a", area);
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(Path.GetFileName(r.GetString(0)));
        }
        catch (Exception e) { _log.LogDebug("No se pudo leer la lista de PDF almacenados de {Area}: {Msg}", area, e.Message); }
        return set;
    }

    private static bool EsAlmacenado(string nombre, HashSet<string> almacenados) =>
        almacenados.Contains(nombre) || ReAlmacenado().IsMatch(nombre);

    private static string DestinoUnico(string dir, string nombre)
    {
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, nombre);
        if (!File.Exists(dest)) return dest;
        var stem = Path.GetFileNameWithoutExtension(nombre);
        var suf = Path.GetExtension(nombre);
        var n = 1;
        while (true)
        {
            var cand = Path.Combine(dir, $"{stem}_{n}{suf}");
            if (!File.Exists(cand)) return cand;
            n++;
        }
    }

    private static string? MoverSilencioso(string origen, string destinoDir)
    {
        try
        {
            if (!File.Exists(origen)) return null;
            var dest = DestinoUnico(destinoDir, Path.GetFileName(origen));
            File.Move(origen, dest);
            return dest;
        }
        catch (FileNotFoundException) { return null; }
        catch (Exception) { return null; }
    }

    // ------------------------------------------------------------------ Excel
    /// Importa los PDFs listados en un Excel y aplica correcciones solo a los recién creados.
    /// Columna obligatoria: archivo/ruta. Opcionales: area, folio, fecha, nombre, nss, agregado, modalidad, estudio.
    public ResultadoExcel ImportarExcel(Stream xlsx, string areaDefecto)
    {
        if (!PatoDb.Areas.ContainsKey(areaDefecto)) areaDefecto = "esp";
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheets.FirstOrDefault()
                 ?? throw new InvalidOperationException("El Excel no tiene hojas.");

        var maxCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var maxRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        // 1) Localizar la fila de encabezado y el mapa de columnas.
        var colmap = new Dictionary<string, int>();
        var headerRow = 0;
        for (var r = 1; r <= maxRow && headerRow == 0; r++)
        {
            var m = new Dictionary<string, int>();
            for (var c = 1; c <= maxCol; c++)
            {
                var n = NormCol(ws.Cell(r, c).GetString());
                if (n.Length == 0) continue;
                foreach (var (campo, alias) in ColsExcel)
                    if (!m.ContainsKey(campo) && alias.Contains(n)) m[campo] = c;
            }
            if (m.ContainsKey("archivo")) { colmap = m; headerRow = r; }
        }
        if (headerRow == 0)
            throw new InvalidOperationException("El Excel no tiene una columna 'archivo' o 'ruta' con la ubicación de cada PDF.");

        string Celda(int r, string campo) =>
            colmap.TryGetValue(campo, out var c) ? ws.Cell(r, c).GetString().Trim() : "";

        var filas = new List<FilaExcel>();
        int nuevos = 0, omitidos = 0, errores = 0, corregidos = 0;

        for (var r = headerRow + 1; r <= maxRow; r++)
        {
            var valArch = Celda(r, "archivo");
            if (valArch.Length == 0) continue;

            var area = Celda(r, "area").ToLowerInvariant();
            if (!PatoDb.Areas.ContainsKey(area)) area = areaDefecto;

            var pdf = ResolverPdf(valArch, area);
            if (pdf is null)
            {
                errores++;
                filas.Add(new FilaExcel(valArch, area, false, null, null, null, null, "PDF no encontrado", false));
                continue;
            }

            ResultadoGuardado g;
            try { g = _repo.Guardar(area, File.ReadAllBytes(pdf), Path.GetFileName(pdf), "excel"); }
            catch (Exception e)
            {
                errores++;
                filas.Add(new FilaExcel(Path.GetFileName(pdf), area, false, null, null, null, null, e.Message, false));
                continue;
            }

            var rid = g.Id;
            bool esNuevo; string aviso;
            if (g.Ok && g.Omitido) { omitidos++; esNuevo = false; aviso = $"ya existía (id {rid})"; }
            else if (g.Ok) { nuevos++; esNuevo = true; aviso = $"id {rid} · folio {g.Folio} · {g.Modalidad}"; }
            else
            {
                errores++;
                filas.Add(new FilaExcel(Path.GetFileName(pdf), area, false, null, null, null, null, g.Error, false));
                continue;
            }

            var corregido = false;
            if (esNuevo && rid is not null)
            {
                var sets = new List<string>();
                var vals = new List<(string, object)>();
                foreach (var campo in CamposCorreccion)
                {
                    var (col, largo) = ColLargo(campo);
                    var v = Celda(r, campo);
                    if (v.Length > 0) { sets.Add($"{col}=@{campo}"); vals.Add(($"@{campo}", PatoDb.Trunc(v, largo))); }
                }
                var fex = FechaExcel(ws, r, colmap);
                if (fex.Length > 0) { sets.Add("fecha=@fecha"); vals.Add(("@fecha", DateOnly.ParseExact(fex, "yyyy-MM-dd"))); }
                if (sets.Contains("nombre=@nombre"))
                {
                    sets.Add("nombre_norm=@nnorm");
                    vals.Add(("@nnorm", PatoDb.Trunc(PatoDb.Normalizar(Celda(r, "nombre")), 200)));
                }
                if (sets.Count > 0)
                {
                    using var cn = _db.Conexion(); cn.Open();
                    using var cmd = cn.CreateCommand();
                    cmd.CommandText = $"UPDATE reportes SET {string.Join(", ", sets)} WHERE id=@id";
                    foreach (var (n, v) in vals) cmd.Parameters.AddWithValue(n, v);
                    cmd.Parameters.AddWithValue("@id", rid);
                    cmd.ExecuteNonQuery();
                    corregido = true; corregidos++;
                }
            }

            filas.Add(new FilaExcel(Path.GetFileName(pdf), area, esNuevo, rid, g.Folio, g.Modalidad, aviso, null, corregido, g.Estudio));
        }

        return new ResultadoExcel(nuevos, omitidos, errores, corregidos, filas);
    }

    private static (string, int) ColLargo(string campo) => campo switch
    {
        "folio" => ("folio", 60),
        "nombre" => ("nombre", 200),
        "nss" => ("nss", 20),
        "agregado" => ("agregado_medico", 20),
        "modalidad" => ("modalidad", 60),
        "estudio" => ("estudio", 255),
        _ => (campo, 60),
    };

    /// En la web no hay "directorio del xlsx": las rutas relativas se buscan en la
    /// carpeta de entrada y en el almacén del área; las absolutas se usan tal cual.
    private string? ResolverPdf(string valor, string area)
    {
        var s = valor.Trim().Trim('"');
        if (s.Length == 0) return null;
        if (Path.IsPathRooted(s)) return File.Exists(s) ? s : null;
        string[] bases = { _db.InboxArea(area), Path.Combine(_db.StorageDir, _db.CarpetaArea(area)) };
        foreach (var b in bases)
        {
            var c = Path.Combine(b, s);
            if (File.Exists(c)) return c;
        }
        return null;
    }

    private static string NormCol(string? s)
    {
        var t = PatoDb.Normalizar(s); // NFKD, sin acentos, mayúsculas
        return new string(t.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string FechaExcel(IXLWorksheet ws, int r, Dictionary<string, int> colmap)
    {
        if (!colmap.TryGetValue("fecha", out var c)) return "";
        var cell = ws.Cell(r, c);
        if (cell.Value.IsDateTime) return cell.Value.GetDateTime().ToString("yyyy-MM-dd");
        var s = cell.GetString().Trim();
        if (s.Length == 0) return "";
        string[] fmts = { "yyyy-MM-dd", "dd/MM/yyyy", "dd/MM/yy", "dd-MM-yyyy" };
        foreach (var f in fmts)
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");
        return "";
    }
}

/// Vigilante que importa automáticamente los PDF que dejan en las carpetas de entrada.
public class ImportadorService : BackgroundService
{
    private readonly Importador _imp;
    private readonly IConfiguration _cfg;
    private readonly ILogger<ImportadorService> _log;

    public ImportadorService(Importador imp, IConfiguration cfg, ILogger<ImportadorService> log)
    {
        _imp = imp; _cfg = cfg; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var w = _cfg["Patologia:WatchInbox"];
        var watch = w is null || !(w == "0" || w.Equals("false", StringComparison.OrdinalIgnoreCase));
        if (!watch) { _log.LogInformation("Vigilancia de entrantes desactivada (Patologia:WatchInbox=false)."); return; }

        var seg = _cfg.GetValue("Patologia:WatchInterval", 30);
        if (seg < 5) seg = 5;
        _log.LogInformation("Vigilando carpetas de entrada cada {Seg}s.", seg);

        while (!ct.IsCancellationRequested)
        {
            try { _imp.EscanearInbox(mover: true); }
            catch (Exception e) { _log.LogError(e, "Error en vigilancia de carpeta."); }
            try { await Task.Delay(TimeSpan.FromSeconds(seg), ct); }
            catch (TaskCanceledException) { break; }
        }
    }
}
