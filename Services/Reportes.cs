using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Histórico de reportes de estatus. Solo se agrega y se consulta: nunca se edita ni se elimina.
public interface IReportStore
{
    /// <param name="itemId">null = todos los elementos.</param>
    IReadOnlyList<ReporteEstatus> Query(long? itemId = null, string? autor = null, DateTime? desde = null, DateTime? hasta = null, int limite = 1000);
    /// <returns>201 con el reporte guardado.</returns>
    (int Status, string? Error, ReporteEstatus? Item) Add(ReporteEstatus borrador);
}

public class JsonReportStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonReportStore> logger) : IReportStore
{
    private sealed class Archivo { public long NextId { get; set; } = 1; public List<ReporteEstatus> Items { get; set; } = new(); }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:ReportFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "reportes.json");
    private Archivo _data = new();
    private bool _cargado = false;

    private Archivo Datos()
    {
        if (_cargado) return _data;
        if (File.Exists(_path))
        {
            try { _data = JsonSerializer.Deserialize<Archivo>(File.ReadAllText(_path), Json) ?? new Archivo(); }
            catch (Exception e) { logger.LogWarning("No se pudo leer reportes.json: {Error}", e.Message); _data = new Archivo(); }
        }
        _cargado = true;
        return _data;
    }

    private void Guardar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_data, Json));
    }

    public IReadOnlyList<ReporteEstatus> Query(long? itemId = null, string? autor = null, DateTime? desde = null, DateTime? hasta = null, int limite = 1000)
    {
        lock (_lock)
        {
            IEnumerable<ReporteEstatus> q = Datos().Items;
            if (itemId is not null) q = q.Where(r => r.ItemId == itemId);
            if (!string.IsNullOrWhiteSpace(autor)) q = q.Where(r => r.Autor.Equals(autor.Trim(), StringComparison.OrdinalIgnoreCase));
            if (desde is not null) q = q.Where(r => r.Momento >= desde.Value.Date);
            if (hasta is not null) q = q.Where(r => r.Momento < hasta.Value.Date.AddDays(1));
            return q.OrderByDescending(r => r.Momento).ThenByDescending(r => r.Id).Take(limite).ToList();
        }
    }

    public (int, string?, ReporteEstatus?) Add(ReporteEstatus borrador)
    {
        lock (_lock)
        {
            var data = Datos();
            var reporte = borrador with { Id = data.NextId++ };
            data.Items.Add(reporte);
            Guardar();
            return (201, null, reporte);
        }
    }
}

public class MySqlReportStore(IConfiguration config) : IReportStore
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
            CREATE TABLE IF NOT EXISTS `{db}`.`reportes_estatus` (
              `id`                BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `item_id`           BIGINT        NOT NULL,
              `momento`           DATETIME(6)   NOT NULL,
              `autor`             VARCHAR(40)   NOT NULL,
              `estado`            VARCHAR(20)   NOT NULL DEFAULT '',
              `avance`            INT           NOT NULL DEFAULT 0,
              `descripcion_avance` VARCHAR(2000) NOT NULL DEFAULT '',
              `problemas`         VARCHAR(2000) NOT NULL DEFAULT '',
              `pendientes`        VARCHAR(2000) NOT NULL DEFAULT '',
              `proximas_acciones` VARCHAR(2000) NOT NULL DEFAULT '',
              `fecha_estimada`    DATETIME      NULL,
              `comentarios`       VARCHAR(2000) NOT NULL DEFAULT '',
              KEY `idx_reportes_item` (`item_id`, `momento`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    private static ReporteEstatus Leer(MySqlDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1),
        DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Unspecified),
        r.GetString(3), r.GetString(4), r.GetInt32(5),
        r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9),
        r.IsDBNull(10) ? null : r.GetDateTime(10).Date,
        r.GetString(11));

    public IReadOnlyList<ReporteEstatus> Query(long? itemId = null, string? autor = null, DateTime? desde = null, DateTime? hasta = null, int limite = 1000)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        var sql = """
            SELECT id, item_id, momento, autor, estado, avance, descripcion_avance, problemas,
                   pendientes, proximas_acciones, fecha_estimada, comentarios
            FROM reportes_estatus
            """;
        var condiciones = new List<string>();
        if (itemId is not null) { condiciones.Add("item_id = @item"); cmd.Parameters.AddWithValue("@item", itemId); }
        if (!string.IsNullOrWhiteSpace(autor)) { condiciones.Add("autor = @autor"); cmd.Parameters.AddWithValue("@autor", autor.Trim()); }
        if (desde is not null) { condiciones.Add("momento >= @desde"); cmd.Parameters.AddWithValue("@desde", desde.Value.Date); }
        if (hasta is not null) { condiciones.Add("momento < @hasta"); cmd.Parameters.AddWithValue("@hasta", hasta.Value.Date.AddDays(1)); }
        if (condiciones.Count > 0) sql += " WHERE " + string.Join(" AND ", condiciones);
        sql += " ORDER BY momento DESC, id DESC LIMIT @lim";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@lim", limite);
        var lista = new List<ReporteEstatus>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Leer(r));
        return lista;
    }

    public (int, string?, ReporteEstatus?) Add(ReporteEstatus borrador)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reportes_estatus (item_id, momento, autor, estado, avance, descripcion_avance,
                problemas, pendientes, proximas_acciones, fecha_estimada, comentarios)
            VALUES (@i, @m, @a, @e, @av, @d, @p, @pe, @pa, @fe, @c);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@i", borrador.ItemId);
        cmd.Parameters.AddWithValue("@m", borrador.Momento == default ? DateTime.Now : borrador.Momento);
        cmd.Parameters.AddWithValue("@a", borrador.Autor);
        cmd.Parameters.AddWithValue("@e", borrador.Estado);
        cmd.Parameters.AddWithValue("@av", borrador.Avance);
        cmd.Parameters.AddWithValue("@d", borrador.DescripcionAvance);
        cmd.Parameters.AddWithValue("@p", borrador.Problemas);
        cmd.Parameters.AddWithValue("@pe", borrador.Pendientes);
        cmd.Parameters.AddWithValue("@pa", borrador.ProximasAcciones);
        cmd.Parameters.AddWithValue("@fe", (object?)borrador.FechaEstimada?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", borrador.Comentarios);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        var guardado = borrador with { Id = id };
        return (201, null, guardado);
    }
}
