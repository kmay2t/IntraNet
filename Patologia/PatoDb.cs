using System.Globalization;
using System.Text;
using MySqlConnector;

namespace Meditechnosys.Intranet.Patologia;

/// Acceso a MySQL (base RepoPato) y al almacenamiento fijo de PDFs.
/// Espejo de app/db.py del antiguo repositorio Python.
public class PatoDb
{
    public static readonly IReadOnlyDictionary<string, string> Areas =
        new Dictionary<string, string> { ["esp"] = "especialidades", ["gineco"] = "ginecologia" };

    private readonly string _cs;
    public string StorageDir { get; }
    public string InboxDir { get; }

    public PatoDb(IConfiguration config)
    {
        _cs = config.GetConnectionString("Patologia")
              ?? throw new InvalidOperationException("Falta ConnectionStrings:Patologia.");
        StorageDir = config["Patologia:StorageDir"]
                     ?? throw new InvalidOperationException("Falta Patologia:StorageDir.");
        InboxDir = config["Patologia:InboxDir"] ?? StorageDir;
    }

    public MySqlConnection Conexion() => new(_cs);

    // ---------------------------------------------------------- rutas
    public string CarpetaArea(string area) =>
        Areas.TryGetValue(area, out var c) ? c : throw new ArgumentException($"Área desconocida: {area}");

    public string InboxArea(string area) => Path.Combine(InboxDir, CarpetaArea(area));
    public string ProcesadosArea(string area) => Path.Combine(InboxArea(area), "procesados");
    public string ErroresArea(string area) => Path.Combine(InboxArea(area), "errores");

    /// Ruta relativa al almacenamiento: especialidades/00000012.pdf
    public static string RutaRelativa(string area, long id) => $"{Areas[area]}/{id:D8}.pdf";

    /// Convierte la ruta guardada en BD a ruta real; impide salir del almacenamiento.
    public string RutaAbsoluta(string rutaRel)
    {
        var storage = Path.GetFullPath(StorageDir);
        var destino = Path.GetFullPath(Path.Combine(storage, rutaRel));
        if (!destino.StartsWith(storage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta fuera del almacenamiento");
        return destino;
    }

    public void AsegurarCarpetas()
    {
        Directory.CreateDirectory(StorageDir);
        Directory.CreateDirectory(InboxDir);
        foreach (var area in Areas.Keys)
        {
            Directory.CreateDirectory(Path.Combine(StorageDir, CarpetaArea(area)));
            Directory.CreateDirectory(InboxArea(area));
            Directory.CreateDirectory(ProcesadosArea(area));
            Directory.CreateDirectory(ErroresArea(area));
        }
    }

    // ---------------------------------------------------------- utilidades
    /// NFKD, sin combinantes, mayúsculas (como db.normalizar de Python).
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var nfkd = texto.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(nfkd.Length);
        foreach (var ch in nfkd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().ToUpperInvariant().Trim();
    }

    /// La auditoría nunca debe tumbar una operación.
    public void Auditar(string accion, string detalle = "", string ip = "", string usuario = "")
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO auditoria (usuario, accion, detalle, ip) VALUES (@u,@a,@d,@i)";
            cmd.Parameters.AddWithValue("@u", Trunc(usuario, 60));
            cmd.Parameters.AddWithValue("@a", Trunc(accion, 40));
            cmd.Parameters.AddWithValue("@d", Trunc(detalle, 255));
            cmd.Parameters.AddWithValue("@i", Trunc(ip, 45));
            cmd.ExecuteNonQuery();
        }
        catch { /* silencioso, como en Python */ }
    }

    public static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n];
}
