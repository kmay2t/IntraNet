using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Historial de cambios de actividades/tickets (auditoría de seguimiento). Solo se agrega:
/// nunca se modifica ni se elimina ("no eliminar históricos"). Registrar NUNCA lanza.
public interface IHistorialStore
{
    void Registrar(long itemId, string usuario, string campo, string antes, string despues, string comentario = "");
    IReadOnlyList<Cambio> PorItem(long itemId);

    /// Último comentario u observación de cada elemento, para el recordatorio de seguimiento de 3 días.
    IReadOnlyDictionary<long, DateTime> UltimosComentarios();

    /// Lo mismo pero de un solo elemento (devuelve null si nunca le han comentado).
    DateTime? UltimoComentario(long itemId);
}

public abstract class HistorialBase(ILogger logger) : IHistorialStore
{
    public abstract void Registrar(long itemId, string usuario, string campo, string antes, string despues, string comentario = "");
    public abstract IReadOnlyList<Cambio> PorItem(long itemId);
    public abstract IReadOnlyDictionary<long, DateTime> UltimosComentarios();
    public abstract DateTime? UltimoComentario(long itemId);

    /// Los campos del historial que cuentan como "le escribieron algo al elemento": el comentario del
    /// responsable y la observación del que administra. Son los que reinician el recordatorio de 3 días.
    protected static readonly string[] CamposComentario = { "Comentario", "Observación" };

    protected static bool EsComentario(string? campo) =>
        campo is not null && CamposComentario.Contains(campo, StringComparer.Ordinal);

    protected static string Recortar(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    protected void Fallo(string operacion, Exception e) =>
        logger.LogWarning("Fallo al {Operacion} en el historial: {Error}", operacion, e.Message);
}

/// Respaldo en archivo JSONL (una línea por cambio): App_Data/historial.jsonl.
public class JsonHistorialLog(IWebHostEnvironment env, IConfiguration config, ILogger<JsonHistorialLog> logger)
    : HistorialBase(logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:HistorialFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "historial.jsonl");
    private long _secuencia = 0;

    public override void Registrar(long itemId, string usuario, string campo, string antes, string despues, string comentario = "")
    {
        var cambio = new Cambio(Interlocked.Increment(ref _secuencia), itemId, DateTime.Now,
            usuario ?? "", Recortar(campo, 60), Recortar(antes, 500), Recortar(despues, 500), Recortar(comentario, 1000));
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, JsonSerializer.Serialize(cambio, Json) + Environment.NewLine);
            }
        }
        catch (Exception e) { Fallo("escribir", e); }
    }

    public override IReadOnlyList<Cambio> PorItem(long itemId)
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return Array.Empty<Cambio>();
                return File.ReadAllLines(_path)
                    .Where(l => l.Length > 0)
                    .Select(l => { try { return JsonSerializer.Deserialize<Cambio>(l, Json); } catch { return null; } })
                    .Where(c => c is not null && c.ItemId == itemId)
                    .OrderBy(c => c!.Momento).ThenBy(c => c!.Id)
                    .Select(c => c!)
                    .ToList();
            }
        }
        catch (Exception e) { Fallo("leer", e); return Array.Empty<Cambio>(); }
    }

    public override IReadOnlyDictionary<long, DateTime> UltimosComentarios()
    {
        var mapa = new Dictionary<long, DateTime>();
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return mapa;
                foreach (var linea in File.ReadAllLines(_path))
                {
                    if (linea.Length == 0) continue;
                    Cambio? c;
                    try { c = JsonSerializer.Deserialize<Cambio>(linea, Json); } catch { continue; }
                    if (c is null || !EsComentario(c.Campo)) continue;
                    if (!mapa.TryGetValue(c.ItemId, out var previo) || c.Momento > previo) mapa[c.ItemId] = c.Momento;
                }
            }
        }
        catch (Exception e) { Fallo("leer los últimos comentarios", e); }
        return mapa;
    }

    public override DateTime? UltimoComentario(long itemId) =>
        UltimosComentarios().TryGetValue(itemId, out var momento) ? momento : null;
}

/// Historial en MySQL (misma base de usuarios): tabla `historial_cambios`.
public class MySqlHistorialLog(IConfiguration config, ILogger<MySqlHistorialLog> logger) : HistorialBase(logger)
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`historial_cambios` (
              `id`         BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `item_id`    BIGINT        NOT NULL,
              `momento`    DATETIME(6)   NOT NULL,
              `usuario`    VARCHAR(40)   NOT NULL DEFAULT '',
              `campo`      VARCHAR(60)   NOT NULL,
              `antes`      VARCHAR(500)  NOT NULL DEFAULT '',
              `despues`    VARCHAR(500)  NOT NULL DEFAULT '',
              `comentario` VARCHAR(1000) NOT NULL DEFAULT '',
              KEY `idx_historial_item` (`item_id`, `momento`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public override void Registrar(long itemId, string usuario, string campo, string antes, string despues, string comentario = "")
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO historial_cambios (item_id, momento, usuario, campo, antes, despues, comentario)
                VALUES (@i, @m, @u, @c, @a, @d, @co)
                """;
            cmd.Parameters.AddWithValue("@i", itemId);
            cmd.Parameters.AddWithValue("@m", DateTime.Now);
            cmd.Parameters.AddWithValue("@u", Recortar(usuario, 40));
            cmd.Parameters.AddWithValue("@c", Recortar(campo, 60));
            cmd.Parameters.AddWithValue("@a", Recortar(antes, 500));
            cmd.Parameters.AddWithValue("@d", Recortar(despues, 500));
            cmd.Parameters.AddWithValue("@co", Recortar(comentario, 1000));
            cmd.ExecuteNonQuery();
        }
        catch (Exception e) { Fallo("escribir", e); }
    }

    public override IReadOnlyList<Cambio> PorItem(long itemId)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT id, item_id, momento, usuario, campo, antes, despues, comentario FROM historial_cambios WHERE item_id = @i ORDER BY momento, id";
            cmd.Parameters.AddWithValue("@i", itemId);
            var lista = new List<Cambio>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new Cambio(r.GetInt64(0), r.GetInt64(1),
                    DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Unspecified),
                    r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7)));
            return lista;
        }
        catch (Exception e) { Fallo("leer", e); return Array.Empty<Cambio>(); }
    }

    /// `campo IN (@c0,@c1)` con sus parámetros ya cargados, para no escribir el acento a mano dos veces.
    private void FiltroComentarios(MySqlCommand cmd, string prefijo)
    {
        var marcas = new string[CamposComentario.Length];
        for (var i = 0; i < CamposComentario.Length; i++)
        {
            marcas[i] = prefijo + i;
            cmd.Parameters.AddWithValue(marcas[i], CamposComentario[i]);
        }
        cmd.CommandText = cmd.CommandText.Replace($"{{{prefijo}lista}}", string.Join(",", marcas));
    }

    public override IReadOnlyDictionary<long, DateTime> UltimosComentarios()
    {
        var mapa = new Dictionary<long, DateTime>();
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT item_id, MAX(momento) FROM historial_cambios WHERE campo IN ({pclista}) GROUP BY item_id";
            FiltroComentarios(cmd, "@pc");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (!r.IsDBNull(1))
                    mapa[r.GetInt64(0)] = DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Unspecified);
        }
        catch (Exception e) { Fallo("leer los últimos comentarios", e); }
        return mapa;
    }

    public override DateTime? UltimoComentario(long itemId)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT MAX(momento) FROM historial_cambios WHERE item_id = @i AND campo IN ({pclista})";
            cmd.Parameters.AddWithValue("@i", itemId);
            FiltroComentarios(cmd, "@pc");
            var valor = cmd.ExecuteScalar();
            return valor is DateTime m ? DateTime.SpecifyKind(m, DateTimeKind.Unspecified) : null;
        }
        catch (Exception e) { Fallo("leer el último comentario", e); return null; }
    }
}
