using System.Text.Json;
using Meditechnosys.Intranet.Models;
using Microsoft.AspNetCore.Http;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Bitacora de accesos: registra inicios/cierres de sesion y los movimientos de cada
/// usuario para poder consultarlos por usuario y por dia. Tiene dos almacenes con la misma
/// semantica (igual que los usuarios): MySQL o un archivo JSONL de respaldo.
/// Registrar NUNCA lanza: un fallo de bitacora no debe romper un login ni una peticion.
public interface IAuditLog
{
    /// <param name="ip">IP del visitante; si se omite, se toma de la petición en curso.</param>
    /// <param name="dispositivo">
    /// Descripción del equipo (ver <see cref="Dispositivo"/>); si se omite, se arma con el
    /// User-Agent de la petición en curso. En un trabajo de segundo plano queda vacía.
    /// </param>
    void Register(string usuario, string evento, string detalle = "", string? ip = null,
        string? dispositivo = null);
    /// <param name="dia">Dia local a filtrar (null = todos).</param>
    /// <param name="desde">Primer dia local del rango (inclusive); null = sin limite por abajo.</param>
    /// <param name="hasta">Ultimo dia local del rango (inclusive); null = sin limite por arriba.</param>
    IReadOnlyList<AuditEntry> Query(string? usuario = null, DateTime? dia = null, int limite = 500,
        DateTime? desde = null, DateTime? hasta = null);
    /// <summary>IPs distintas que aparecen en la bitácora con cuántos movimientos cada una.</summary>
    IReadOnlyList<IpUso> IpsRegistradas(int limite = 400);
    /// <summary>
    /// Cuántos registros dejó cada día local ese usuario (clave `yyyy-MM-dd`). Es el índice que le
    /// dice a la pantalla en qué días, meses y años hay historia real, sin bajar los renglones.
    /// </summary>
    IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null);
    /// <summary>
    /// Último movimiento de cada usuario, con su IP y su dispositivo. Sirve para decir desde dónde
    /// está trabajando ahora mismo cada quien, sin tener que abrir su lista de movimientos.
    /// </summary>
    IReadOnlyDictionary<string, AuditEntry> UltimosDetalles();
    /// Ultimo "Inicio de sesion" registrado por cada usuario (para mostrar "ultima vez que entro").
    IReadOnlyDictionary<string, DateTime> UltimosAccesos();
    /// Ultimo movimiento (de cualquier tipo) registrado por cada usuario. Sirve para
    /// deducir si esta "Conectado" (actividad reciente) o "Desconectado".
    IReadOnlyDictionary<string, DateTime> UltimasActividades();
}

public abstract class AuditLogBase(ILogger logger, IHttpContextAccessor? http) : IAuditLog
{
    public abstract void Register(string usuario, string evento, string detalle = "", string? ip = null,
        string? dispositivo = null);
    public abstract IReadOnlyList<AuditEntry> Query(string? usuario = null, DateTime? dia = null,
        int limite = 500, DateTime? desde = null, DateTime? hasta = null);
    public abstract IReadOnlyList<IpUso> IpsRegistradas(int limite = 400);
    public abstract IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null);
    public abstract IReadOnlyDictionary<string, AuditEntry> UltimosDetalles();
    public abstract IReadOnlyDictionary<string, DateTime> UltimosAccesos();
    public abstract IReadOnlyDictionary<string, DateTime> UltimasActividades();

    /// <summary>
    /// IP y descripción del dispositivo de la petición que se está atendiendo. Se leen aquí y no en
    /// cada llamada para que los decenas de puntos que registran eventos no tengan que hacerse cargo
    /// del encabezado. Fuera de una petición (un trabajo de segundo plano) devuelven cadena vacía.
    /// </summary>
    protected (string Ip, string Dispositivo) DatosDeLaPeticion(string? ipEscrita)
    {
        var ctx = http?.HttpContext;
        var ip = string.IsNullOrWhiteSpace(ipEscrita) ? ctx?.Connection.RemoteIpAddress?.ToString() : ipEscrita;
        return (Recortar(ip, 45), Dispositivo.Describir(ctx?.Request.Headers.UserAgent.ToString()));
    }

    /// Convierte un dia local en el rango UTC [inicio, inicio siguiente) para filtrar.
    protected static (DateTime Desde, DateTime Hasta) RangoDeDia(DateTime dia)
    {
        var local = DateTime.SpecifyKind(dia.Date, DateTimeKind.Local);
        return (local.ToUniversalTime(), local.AddDays(1).ToUniversalTime());
    }

    /// Rango UTC [inclusive, exclusive) que cubre los dias locales desde..hasta, los dos incluidos.
    /// Es lo que empuja a la base el filtro por mes, por ano o por rango de fechas de la ventana;
    /// cuando no hay ninguno de los dos devuelve null y la consulta queda sin limite de tiempo.
    protected static (DateTime Desde, DateTime Hasta)? RangoDePeriodo(DateTime? desde, DateTime? hasta)
    {
        if (desde is null && hasta is null) return null;
        var d = desde is null ? DateTime.MinValue
            : DateTime.SpecifyKind(desde.Value.Date, DateTimeKind.Local).ToUniversalTime();
        var h = hasta is null ? DateTime.MaxValue
            : DateTime.SpecifyKind(hasta.Value.Date, DateTimeKind.Local).AddDays(1).ToUniversalTime();
        return (d, h);
    }

    /// Offset de la maquina en el formato que entiende CONVERT_TZ de MySQL ('+06:00' / '-06:00'), para
    /// agrupar por dia local y no por dia UTC: un movimiento de las 20:00 de aqui es otro dia alla.
    protected static string OffsetLocalParaSql()
    {
        var o = TimeZoneInfo.Local.BaseUtcOffset;
        var t = o < TimeSpan.Zero ? o.Negate() : o;
        return $"{(o < TimeSpan.Zero ? '-' : '+')}{t.Hours:D2}:{t.Minutes:D2}";
    }

    protected static string Recortar(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    protected void Fallo(string operacion, Exception e) =>
        logger.LogWarning("Fallo al {Operacion} en la bitacora: {Error}", operacion, e.Message);
}

/// Almacen de respaldo en archivo JSONL (una linea por evento): escribir es agregar una
/// linea, sin reescribir todo el archivo. Ruta: App_Data/bitacora.jsonl (configurable).
public class JsonAuditLog(IWebHostEnvironment env, IConfiguration config, ILogger<JsonAuditLog> logger,
    IHttpContextAccessor http) : AuditLogBase(logger, http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:AuditFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "bitacora.jsonl");
    private long _secuencia = 0;

    public override void Register(string usuario, string evento, string detalle = "", string? ip = null,
        string? dispositivo = null)
    {
        var (ipReal, disp) = DatosDeLaPeticion(ip);
        var textoDisp = string.IsNullOrWhiteSpace(dispositivo) ? disp : dispositivo!;
        var entrada = new AuditEntry(Interlocked.Increment(ref _secuencia), DateTime.UtcNow,
            usuario ?? "", evento ?? "", Recortar(detalle, 255), ipReal,
            Recortar(textoDisp, Dispositivo.Max));
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, JsonSerializer.Serialize(entrada, Json) + Environment.NewLine);
            }
        }
        catch (Exception e) { Fallo("escribir", e); }
    }

    public override IReadOnlyList<AuditEntry> Query(string? usuario = null, DateTime? dia = null,
        int limite = 500, DateTime? desde = null, DateTime? hasta = null)
    {
        try
        {
            return Filtrar(LeerTodas(), usuario, dia, limite, desde, hasta);
        }
        catch (Exception e) { Fallo("leer", e); return Array.Empty<AuditEntry>(); }
    }

    public override IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null)
    {
        try
        {
            IEnumerable<AuditEntry> q = LeerTodas();
            if (!string.IsNullOrWhiteSpace(usuario))
                q = q.Where(e => e.Usuario.Equals(usuario.Trim(), StringComparison.OrdinalIgnoreCase));
            return q.GroupBy(e => e.Momento.ToLocalTime().ToString("yyyy-MM-dd"), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        }
        catch (Exception e) { Fallo("leer dias", e); return new Dictionary<string, int>(); }
    }

    public override IReadOnlyList<IpUso> IpsRegistradas(int limite = 400)
    {
        try
        {
            return LeerTodas().Where(e => e.Ip.Length > 0)
                .GroupBy(e => e.Ip, StringComparer.OrdinalIgnoreCase)
                .Select(g => new IpUso(g.Key, g.Count()))
                .OrderByDescending(x => x.Usos).Take(limite).ToList();
        }
        catch (Exception e) { Fallo("leer ips", e); return Array.Empty<IpUso>(); }
    }

    public override IReadOnlyDictionary<string, AuditEntry> UltimosDetalles()
    {
        try
        {
            return LeerTodas()
                .Where(e => e.Usuario.Length > 0)
                .GroupBy(e => e.Usuario, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key,
                    g => g.OrderByDescending(e => e.Momento).ThenByDescending(e => e.Id).First(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) { Fallo("leer últimos detalles", e); return new Dictionary<string, AuditEntry>(); }
    }

    public override IReadOnlyDictionary<string, DateTime> UltimosAccesos()
    {
        try
        {
            return LeerTodas()
                .Where(e => e.Evento == AuditEvents.Login)
                .GroupBy(e => e.Usuario, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(e => e.Momento), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) { Fallo("leer ultimos accesos", e); return new Dictionary<string, DateTime>(); }
    }

    public override IReadOnlyDictionary<string, DateTime> UltimasActividades()
    {
        try
        {
            return LeerTodas()
                .GroupBy(e => e.Usuario, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(e => e.Momento), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) { Fallo("leer ultimas actividades", e); return new Dictionary<string, DateTime>(); }
    }

    /// Lee todas las lineas validas del JSONL (descarta las corruptas).
    private List<AuditEntry> LeerTodas()
    {
        lock (_lock)
        {
            return !File.Exists(_path) ? new() :
                File.ReadAllLines(_path)
                    .Where(l => l.Length > 0)
                    .Select(l => { try { return JsonSerializer.Deserialize<AuditEntry>(l, Json); } catch { return null; } })
                    .Where(e => e is not null)
                    .Select(e => e!)
                    .ToList();
        }
    }

    internal static IReadOnlyList<AuditEntry> Filtrar(IEnumerable<AuditEntry> todas, string? usuario,
        DateTime? dia, int limite, DateTime? desde = null, DateTime? hasta = null)
    {
        IEnumerable<AuditEntry> q = todas;
        if (!string.IsNullOrWhiteSpace(usuario))
            q = q.Where(e => e.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
        if (dia is not null)
        {
            var (d0, d1) = RangoDeDia(dia.Value);
            q = q.Where(e => e.Momento >= d0 && e.Momento < d1);
        }
        var periodo = RangoDePeriodo(desde, hasta);
        if (periodo is not null)
        {
            var (p0, p1) = periodo.Value;
            q = q.Where(e => e.Momento >= p0 && e.Momento < p1);
        }
        return q.OrderByDescending(e => e.Momento).Take(limite).ToList();
    }
}

/// Almacen en MySQL (misma base de usuarios): tabla `bitacora` con id autonumerico.
public class MySqlAuditLog(IConfiguration config, ILogger<MySqlAuditLog> logger, IHttpContextAccessor http)
    : AuditLogBase(logger, http)
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    /// <summary>
    /// false si la columna `dispositivo` no se pudo crear (por ejemplo porque el usuario de la base
    /// no tiene permisos de ALTER). Mientras no exista, se sigue escribiendo y leyendo la bitacora
    /// sin ese dato, en lugar de fallar la pantalla entera.
    /// </summary>
    private bool _tieneDispositivo = true;

    /// <summary>Crea la tabla de bitacora si no existe y le agrega la columna `dispositivo` (la base ya la crea el almacen de usuarios).</summary>
    public void EnsureSchema()
    {
        var b = new MySqlConnectionStringBuilder(_cs);
        var db = b.Database;
        using var cn = Conexion();
        cn.Open();
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS `{db}`.`bitacora` (
                  `id`      BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                  `momento` DATETIME(6) NOT NULL,
                  `usuario` VARCHAR(40) NOT NULL,
                  `evento`  VARCHAR(60) NOT NULL,
                  `detalle` VARCHAR(255) NOT NULL DEFAULT '',
                  `ip`      VARCHAR(45) NOT NULL DEFAULT '',
                  KEY `idx_bitacora_usuario_momento` (`usuario`, `momento`)
                ) ENGINE=InnoDB;
                """;
            cmd.ExecuteNonQuery();
        }

        // Las tablas ya creadas en produccion no traen `dispositivo`: se agrega una sola vez.
        // Es una columna con valor por defecto, asi que las 4 mil y tantas filas viejas no se tocan.
        try
        {
            using var col = cn.CreateCommand();
            col.CommandText = $"""
                SELECT COUNT(*) FROM information_schema.COLUMNS
                 WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'bitacora' AND COLUMN_NAME = 'dispositivo'
                """;
            col.Parameters.AddWithValue("@db", db);
            var existe = Convert.ToInt64(col.ExecuteScalar() ?? 0L) > 0;
            if (!existe)
            {
                using var add = cn.CreateCommand();
                add.CommandText = $"""
                    ALTER TABLE `{db}`.`bitacora`
                      ADD COLUMN `dispositivo` VARCHAR({Dispositivo.Max}) NOT NULL DEFAULT '' AFTER `ip`
                    """;
                add.ExecuteNonQuery();
            }
        }
        catch (Exception e)
        {
            _tieneDispositivo = false;
            Fallo("agregar la columna dispositivo", e);
        }
    }

    public override void Register(string usuario, string evento, string detalle = "", string? ip = null,
        string? dispositivo = null)
    {
        var (ipReal, disp) = DatosDeLaPeticion(ip);
        var textoDisp = Recortar(string.IsNullOrWhiteSpace(dispositivo) ? disp : dispositivo!, Dispositivo.Max);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = _tieneDispositivo
                ? "INSERT INTO bitacora (momento, usuario, evento, detalle, ip, dispositivo) VALUES (@m, @u, @e, @d, @i, @s)"
                : "INSERT INTO bitacora (momento, usuario, evento, detalle, ip) VALUES (@m, @u, @e, @d, @i)";
            cmd.Parameters.AddWithValue("@m", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@u", usuario ?? "");
            cmd.Parameters.AddWithValue("@e", Recortar(evento, 60));
            cmd.Parameters.AddWithValue("@d", Recortar(detalle, 255));
            cmd.Parameters.AddWithValue("@i", ipReal);
            if (_tieneDispositivo) cmd.Parameters.AddWithValue("@s", textoDisp);
            cmd.ExecuteNonQuery();
        }
        catch (Exception e) { Fallo("escribir", e); }
    }

    public override IReadOnlyList<AuditEntry> Query(string? usuario = null, DateTime? dia = null,
        int limite = 500, DateTime? desde = null, DateTime? hasta = null)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            var sql = "SELECT id, momento, usuario, evento, detalle, ip" +
                      (_tieneDispositivo ? ", dispositivo" : "") + " FROM bitacora";
            var condiciones = new List<string>();
            if (!string.IsNullOrWhiteSpace(usuario)) { condiciones.Add("usuario = @u"); cmd.Parameters.AddWithValue("@u", usuario.Trim()); }
            if (dia is not null)
            {
                var (d0, d1) = RangoDeDia(dia.Value);
                condiciones.Add("momento >= @desde AND momento < @hasta");
                cmd.Parameters.AddWithValue("@desde", d0);
                cmd.Parameters.AddWithValue("@hasta", d1);
            }
            // Mes, año o rango de fechas: se empuja a la tabla (hay índice por usuario y momento),
            // así un periodo viejo no queda atrapado detrás del tope de renglones.
            var periodo = RangoDePeriodo(desde, hasta);
            if (periodo is not null)
            {
                var (p0, p1) = periodo.Value;
                condiciones.Add("momento >= @pDesde AND momento < @pHasta");
                cmd.Parameters.AddWithValue("@pDesde", p0);
                cmd.Parameters.AddWithValue("@pHasta", p1);
            }
            if (condiciones.Count > 0) sql += " WHERE " + string.Join(" AND ", condiciones);
            sql += " ORDER BY momento DESC, id DESC LIMIT @lim";
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@lim", limite);
            var lista = new List<AuditEntry>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lista.Add(new AuditEntry(r.GetInt64(0), DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc),
                    r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                    _tieneDispositivo ? r.GetString(6) : ""));
            return lista;
        }
        catch (Exception e) { Fallo("leer", e); return Array.Empty<AuditEntry>(); }
    }

    public override IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null)
    {
        var salida = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            // Se agrupa por dia LOCAL (CONVERT_TZ con el desfase de la maquina), no por dia UTC:
            // lo que paso a las 20:00 de aqui cuenta en su dia de aqui, no en el de manana.
            cmd.Parameters.AddWithValue("@off", OffsetLocalParaSql());
            var sql = "SELECT DATE_FORMAT(CONVERT_TZ(momento, '+00:00', @off), '%Y-%m-%d') d, COUNT(*) n" +
                      " FROM bitacora";
            var condiciones = new List<string>();
            if (!string.IsNullOrWhiteSpace(usuario)) { condiciones.Add("usuario = @u"); cmd.Parameters.AddWithValue("@u", usuario.Trim()); }
            if (condiciones.Count > 0) sql += " WHERE " + string.Join(" AND ", condiciones);
            cmd.CommandText = sql + " GROUP BY d ORDER BY d";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (!r.IsDBNull(0)) salida[r.GetString(0)] = Convert.ToInt32(r.GetValue(1));
        }
        catch (Exception e) { Fallo("leer dias", e); }
        return salida;
    }

    /// <summary>IPs distintas registradas, con cuántos movimientos tiene cada una (más usadas primero).</summary>
    public override IReadOnlyList<IpUso> IpsRegistradas(int limite = 400)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                SELECT ip, COUNT(*) FROM bitacora WHERE ip <> '' GROUP BY ip ORDER BY COUNT(*) DESC LIMIT @lim
                """;
            cmd.Parameters.AddWithValue("@lim", limite);
            var lista = new List<IpUso>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) lista.Add(new IpUso(r.GetString(0), Convert.ToInt32(r.GetValue(1))));
            return lista;
        }
        catch (Exception e) { Fallo("leer ips", e); return Array.Empty<IpUso>(); }
    }

    /// <summary>
    /// Última fila de cada usuario. Se toma el id más alto por usuario (es autonumérico, así que
    /// coincide con lo más reciente) en lugar del momento, porque dos eventos del mismo segundo
    /// dejarían ambiguous la consulta.
    /// </summary>
    public override IReadOnlyDictionary<string, AuditEntry> UltimosDetalles()
    {
        var mapa = new Dictionary<string, AuditEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                SELECT b.usuario, b.id, b.momento, b.evento, b.detalle, b.ip" +
                (_tieneDispositivo ? ", b.dispositivo" : "") + @"
                  FROM bitacora b
                  JOIN (SELECT usuario, MAX(id) id_max FROM bitacora GROUP BY usuario) u ON u.id_max = b.id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                mapa[r.GetString(0)] = new AuditEntry(r.GetInt64(1),
                    DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc), r.GetString(0),
                    r.GetString(3), r.GetString(4), r.GetString(5),
                    _tieneDispositivo ? r.GetString(6) : "");
            return mapa;
        }
        catch (Exception e) { Fallo("leer últimos detalles", e); return mapa; }
    }

    public override IReadOnlyDictionary<string, DateTime> UltimosAccesos()
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT usuario, MAX(momento) FROM bitacora WHERE evento = @ev GROUP BY usuario";
            cmd.Parameters.AddWithValue("@ev", AuditEvents.Login);
            var mapa = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                mapa[r.GetString(0)] = DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc);
            return mapa;
        }
        catch (Exception e) { Fallo("leer ultimos accesos", e); return new Dictionary<string, DateTime>(); }
    }

    public override IReadOnlyDictionary<string, DateTime> UltimasActividades()
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT usuario, MAX(momento) FROM bitacora GROUP BY usuario";
            var mapa = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                mapa[r.GetString(0)] = DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc);
            return mapa;
        }
        catch (Exception e) { Fallo("leer ultimas actividades", e); return new Dictionary<string, DateTime>(); }
    }
}
