using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Bitácora del quemador DICOM: quién usó el programa, desde qué equipo y qué IP,
/// qué buscó, qué estudios descargó y en qué USB o grabadora los dejó.
///
/// Igual que el resto de almacenes, tiene dos implementaciones con la misma semántica
/// (MySQL y un JSONL de respaldo) elegidas una sola vez al arrancar.
/// Registrar NUNCA lanza: un fallo al guardar no puede romper una grabación en curso.
public interface IQuemadorLog
{
    /// Guarda un lote de eventos y devuelve cuántos quedaron registrados.
    int Registrar(IReadOnlyList<QuemadorEvento> eventos);
    IReadOnlyList<QuemadorEvento> Consultar(QuemadorFiltro filtro);
    /// Usuarios y equipos distintos que aparecen en la bitácora, para poblar los filtros.
    (IReadOnlyList<string> Usuarios, IReadOnlyList<string> Equipos) Valores();
    /// <summary>
    /// Cuántos renglones dejó cada día local ese usuario (clave `yyyy-MM-dd`), o todos si no se dice
    /// cuál. Junto con el de accesos arma el índice que usa la ventana para saber en qué días, meses y
    /// años hay historia antes de que se cargue un solo renglón.
    /// </summary>
    IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null);
}

public abstract class QuemadorLogBase(ILogger logger) : IQuemadorLog
{
    public abstract int Registrar(IReadOnlyList<QuemadorEvento> eventos);
    public abstract IReadOnlyList<QuemadorEvento> Consultar(QuemadorFiltro filtro);
    public abstract (IReadOnlyList<string> Usuarios, IReadOnlyList<string> Equipos) Valores();
    public abstract IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null);

    // Longitudes de las columnas VARCHAR de la tabla; el JSON usa las mismas para que
    // ambas implementaciones devuelvan exactamente lo mismo.
    protected const int MaxEquipo = 80, MaxIp = 45, MaxIpLocal = 160, MaxEvento = 40,
        MaxResultado = 20, MaxLote = 40, MaxPaciente = 160, MaxPacienteId = 60,
        MaxAcceso = 60, MaxUid = 128, MaxModalidad = 40, MaxFecha = 16,
        MaxMedio = 20, MaxUnidad = 80, MaxMensaje = 255;

    /// Deja el evento con las mismas medidas que la tabla y los nombres canónicos,
    /// para que un cliente desactualizado no meta variantes de un mismo evento.
    protected static QuemadorEvento Normalizar(QuemadorEvento e) => e with
    {
        Momento = e.Momento == default ? DateTime.UtcNow : e.Momento.ToUniversalTime(),
        Usuario = Recortar(e.Usuario, 40),
        Equipo = Recortar(e.Equipo, MaxEquipo),
        Ip = Recortar(e.Ip, MaxIp),
        IpLocal = Recortar(e.IpLocal, MaxIpLocal),
        Evento = QuemadorEventos.EsConocido(e.Evento) ? e.Evento! : Recortar(e.Evento, MaxEvento),
        Resultado = QuemadorResultados.Normalizar(e.Resultado),
        Lote = Recortar(e.Lote, MaxLote),
        Paciente = Recortar(e.Paciente, MaxPaciente),
        PacienteId = Recortar(e.PacienteId, MaxPacienteId),
        Acceso = Recortar(e.Acceso, MaxAcceso),
        EstudioUid = Recortar(e.EstudioUid, MaxUid),
        Modalidad = Recortar(e.Modalidad, MaxModalidad),
        FechaEstudio = Recortar(e.FechaEstudio, MaxFecha),
        Imagines = Math.Max(0, e.Imagines),
        Medio = Recortar(e.Medio, MaxMedio),
        Unidad = Recortar(e.Unidad, MaxUnidad),
        Mensaje = Recortar(e.Mensaje, MaxMensaje),
    };

    /// Convierte un día local en el rango UTC [inicio, inicio siguiente) para filtrar.
    protected static (DateTime Desde, DateTime Hasta) RangoDeDia(DateTime dia)
    {
        var local = DateTime.SpecifyKind(dia.Date, DateTimeKind.Local);
        return (local.ToUniversalTime(), local.AddDays(1).ToUniversalTime());
    }

    /// Rango UTC [inclusive, exclusive) que cubre los días locales desde..hasta, los dos incluidos.
    /// Es el filtro por mes, por año o por rango de fechas; null cuando no se pidió ninguno.
    protected static (DateTime Desde, DateTime Hasta)? RangoDePeriodo(DateTime? desde, DateTime? hasta)
    {
        if (desde is null && hasta is null) return null;
        var d = desde is null ? DateTime.MinValue
            : DateTime.SpecifyKind(desde.Value.Date, DateTimeKind.Local).ToUniversalTime();
        var h = hasta is null ? DateTime.MaxValue
            : DateTime.SpecifyKind(hasta.Value.Date, DateTimeKind.Local).AddDays(1).ToUniversalTime();
        return (d, h);
    }

    /// Desfase de la máquina en el formato de CONVERT_TZ ('-06:00'), para agrupar por día local.
    protected static string OffsetLocalParaSql()
    {
        var o = TimeZoneInfo.Local.BaseUtcOffset;
        var t = o < TimeSpan.Zero ? o.Negate() : o;
        return $"{(o < TimeSpan.Zero ? '-' : '+')}{t.Hours:D2}:{t.Minutes:D2}";
    }

    /// Filtro en memoria, idéntico al WHERE de MySQL; lo usa el almacén JSON.
    protected static bool Coincide(QuemadorEvento e, QuemadorFiltro f, string? texto)
    {
        if (!string.IsNullOrWhiteSpace(f.Usuario) &&
            !e.Usuario.Equals(f.Usuario, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(f.Equipo) &&
            !e.Equipo.Equals(f.Equipo, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(f.Evento) &&
            !e.Evento.Equals(f.Evento, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(f.Resultado) &&
            !e.Resultado.Equals(f.Resultado, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(f.Lote) &&
            !e.Lote.Equals(f.Lote, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.Dia is not null)
        {
            var (desde, hasta) = RangoDeDia(f.Dia.Value);
            if (e.Momento < desde || e.Momento >= hasta) return false;
        }
        var periodo = RangoDePeriodo(f.Desde, f.Hasta);
        if (periodo is not null &&
            (e.Momento < periodo.Value.Desde || e.Momento >= periodo.Value.Hasta)) return false;
        if (texto is not null)
        {
            var hay = Cadena(e.Paciente).Contains(texto, StringComparison.OrdinalIgnoreCase)
                      || Cadena(e.PacienteId).Contains(texto, StringComparison.OrdinalIgnoreCase)
                      || Cadena(e.Acceso).Contains(texto, StringComparison.OrdinalIgnoreCase)
                      || Cadena(e.EstudioUid).Contains(texto, StringComparison.OrdinalIgnoreCase)
                      || Cadena(e.Unidad).Contains(texto, StringComparison.OrdinalIgnoreCase)
                      || Cadena(e.Mensaje).Contains(texto, StringComparison.OrdinalIgnoreCase);
            if (!hay) return false;
        }
        return true;
    }

    /// Texto de búsqueda normalizado: sin espacios de sobra y sin acentos, para que
    /// "Hector" y "Héctor" encuentren lo mismo aunque el PACS devuelva uno u otro.
    protected static string Cadena(string? s) => NormalizarTexto(s ?? "");

    /// La regla vive en <see cref="Texto"/> para que sea LA MISMA en toda la intranet:
    /// quita tildes y diéresis (Ñ → N), pasa a mayúsculas y colapsa los espacios.
    protected static string NormalizarTexto(string s) => Texto.Normalizar(s);

    protected static string Recortar(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    protected void Fallo(string operacion, Exception e) =>
        logger.LogWarning("Fallo al {Operacion} en la bitácora del quemador: {Error}", operacion, e.Message);
}

/// Almacen de respaldo en archivo JSONL (una linea por evento). Ruta:
/// App_Data/bitacora_quemador.jsonl (configurable con Quemador:AuditFile).
public class JsonQuemadorLog(IWebHostEnvironment env, IConfiguration config, ILogger<JsonQuemadorLog> logger)
    : QuemadorLogBase(logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Quemador:AuditFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "bitacora_quemador.jsonl");
    private long _secuencia = 0;

    public override int Registrar(IReadOnlyList<QuemadorEvento> eventos)
    {
        if (eventos.Count == 0) return 0;
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                foreach (var e in eventos)
                {
                    var entrada = Normalizar(e) with { Id = Interlocked.Increment(ref _secuencia) };
                    File.AppendAllText(_path, JsonSerializer.Serialize(entrada, Json) + Environment.NewLine);
                }
            }
            return eventos.Count;
        }
        catch (Exception ex) { Fallo("escribir", ex); return 0; }
    }

    public override IReadOnlyList<QuemadorEvento> Consultar(QuemadorFiltro filtro)
    {
        try
        {
            var texto = string.IsNullOrWhiteSpace(filtro.Texto) ? null : NormalizarTexto(filtro.Texto.Trim());
            var limite = Math.Clamp(filtro.Limite <= 0 ? 500 : filtro.Limite, 1, 5000);
            return Leer()
                .Where(e => Coincide(e, filtro, texto))
                .OrderByDescending(e => e.Momento).ThenByDescending(e => e.Id)
                .Take(limite)
                .ToList();
        }
        catch (Exception e) { Fallo("leer", e); return Array.Empty<QuemadorEvento>(); }
    }

    public override (IReadOnlyList<string> Usuarios, IReadOnlyList<string> Equipos) Valores()
    {
        try
        {
            var todas = Leer();
            return (Distintas(todas.Select(e => e.Usuario)), Distintas(todas.Select(e => e.Equipo)));
        }
        catch (Exception e) { Fallo("leer valores", e); return (Array.Empty<string>(), Array.Empty<string>()); }
    }

    public override IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null)
    {
        try
        {
            IEnumerable<QuemadorEvento> q = Leer();
            if (!string.IsNullOrWhiteSpace(usuario))
                q = q.Where(e => Cadena(e.Usuario).Equals(usuario.Trim(), StringComparison.OrdinalIgnoreCase));
            return q.GroupBy(e => e.Momento.ToLocalTime().ToString("yyyy-MM-dd"), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        }
        catch (Exception e) { Fallo("leer dias", e); return new Dictionary<string, int>(); }
    }

    private static IReadOnlyList<string> Distintas(IEnumerable<string> valores) =>
        valores.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

    private List<QuemadorEvento> Leer()
    {
        lock (_lock)
        {
            return !File.Exists(_path) ? new() :
                File.ReadAllLines(_path)
                    .Where(l => l.Length > 0)
                    .Select(l => { try { return JsonSerializer.Deserialize<QuemadorEvento>(l, Json); } catch { return null; } })
                    .Where(e => e is not null)
                    .Select(e => e!)
                    .ToList();
        }
    }
}

/// Almacen en MySQL (misma base de usuarios): tabla `bitacora_quemador`, una fila por estudio tocado.
public class MySqlQuemadorLog(IConfiguration config, ILogger<MySqlQuemadorLog> logger)
    : QuemadorLogBase(logger)
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    /// Crea la tabla si no existe (la base ya la crea el almacen de usuarios).
    public void EnsureSchema()
    {
        var b = new MySqlConnectionStringBuilder(_cs);
        var db = b.Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`bitacora_quemador` (
              `id`            BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `momento`       DATETIME(6) NOT NULL,
              `usuario`       VARCHAR(40)  NOT NULL DEFAULT '',
              `equipo`        VARCHAR(80)  NOT NULL DEFAULT '',
              `ip`            VARCHAR(45)  NOT NULL DEFAULT '',
              `ip_local`      VARCHAR(160) NOT NULL DEFAULT '',
              `evento`        VARCHAR(40)  NOT NULL,
              `resultado`     VARCHAR(20)  NOT NULL DEFAULT '',
              `lote`          VARCHAR(40)  NOT NULL DEFAULT '',
              `paciente`      VARCHAR(160) NOT NULL DEFAULT '',
              `paciente_id`   VARCHAR(60)  NOT NULL DEFAULT '',
              `acceso`        VARCHAR(60)  NOT NULL DEFAULT '',
              `estudio_uid`   VARCHAR(128) NOT NULL DEFAULT '',
              `modalidad`     VARCHAR(40)  NOT NULL DEFAULT '',
              `fecha_estudio` VARCHAR(16)  NOT NULL DEFAULT '',
              `imagines`      INT          NOT NULL DEFAULT 0,
              `medio`         VARCHAR(20)  NOT NULL DEFAULT '',
              `unidad`        VARCHAR(80)  NOT NULL DEFAULT '',
              `mensaje`       VARCHAR(255) NOT NULL DEFAULT '',
              KEY `idx_bq_momento` (`momento`),
              KEY `idx_bq_usuario_momento` (`usuario`, `momento`),
              KEY `idx_bq_equipo_momento` (`equipo`, `momento`),
              KEY `idx_bq_lote` (`lote`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public override int Registrar(IReadOnlyList<QuemadorEvento> eventos)
    {
        if (eventos.Count == 0) return 0;
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var tx = cn.BeginTransaction();
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO bitacora_quemador (momento, usuario, equipo, ip, ip_local, evento, resultado,
                  lote, paciente, paciente_id, acceso, estudio_uid, modalidad, fecha_estudio,
                  imagines, medio, unidad, mensaje)
                VALUES (@m, @u, @q, @i, @l, @e, @r, @o, @pn, @pi, @ac, @uid, @mo, @fe, @im, @me, @un, @ms)
                """;
            var p = new Dictionary<string, MySqlParameter>();
            foreach (var n in new[] { "@m", "@u", "@q", "@i", "@l", "@e", "@r", "@o", "@pn", "@pi", "@ac", "@uid", "@mo", "@fe", "@im", "@me", "@un", "@ms" })
                p[n] = cmd.Parameters.AddWithValue(n, "");
            var guardados = 0;
            foreach (var ev in eventos)
            {
                var e = Normalizar(ev);
                p["@m"].Value = e.Momento;
                p["@u"].Value = e.Usuario;
                p["@q"].Value = e.Equipo;
                p["@i"].Value = e.Ip;
                p["@l"].Value = e.IpLocal;
                p["@e"].Value = e.Evento;
                p["@r"].Value = e.Resultado;
                p["@o"].Value = e.Lote;
                p["@pn"].Value = e.Paciente;
                p["@pi"].Value = e.PacienteId;
                p["@ac"].Value = e.Acceso;
                p["@uid"].Value = e.EstudioUid;
                p["@mo"].Value = e.Modalidad;
                p["@fe"].Value = e.FechaEstudio;
                p["@im"].Value = e.Imagines;
                p["@me"].Value = e.Medio;
                p["@un"].Value = e.Unidad;
                p["@ms"].Value = e.Mensaje;
                guardados += cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return guardados;
        }
        catch (Exception ex) { Fallo("escribir", ex); return 0; }
    }

    public override IReadOnlyList<QuemadorEvento> Consultar(QuemadorFiltro filtro)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            var condiciones = new List<string>();
            if (!string.IsNullOrWhiteSpace(filtro.Usuario)) { condiciones.Add("usuario = @u"); cmd.Parameters.AddWithValue("@u", filtro.Usuario.Trim()); }
            if (!string.IsNullOrWhiteSpace(filtro.Equipo)) { condiciones.Add("equipo = @q"); cmd.Parameters.AddWithValue("@q", filtro.Equipo.Trim()); }
            if (!string.IsNullOrWhiteSpace(filtro.Evento)) { condiciones.Add("evento = @e"); cmd.Parameters.AddWithValue("@e", filtro.Evento.Trim()); }
            if (!string.IsNullOrWhiteSpace(filtro.Resultado)) { condiciones.Add("resultado = @r"); cmd.Parameters.AddWithValue("@r", filtro.Resultado.Trim()); }
            if (!string.IsNullOrWhiteSpace(filtro.Lote)) { condiciones.Add("lote = @lo"); cmd.Parameters.AddWithValue("@lo", filtro.Lote.Trim()); }
            if (filtro.Dia is not null)
            {
                var (desde, hasta) = RangoDeDia(filtro.Dia.Value);
                condiciones.Add("momento >= @desde AND momento < @hasta");
                cmd.Parameters.AddWithValue("@desde", desde);
                cmd.Parameters.AddWithValue("@hasta", hasta);
            }
            // Mes, año o rango de fechas de la ventana: también se empuja a la tabla.
            var periodo = RangoDePeriodo(filtro.Desde, filtro.Hasta);
            if (periodo is not null)
            {
                var (p0, p1) = periodo.Value;
                condiciones.Add("momento >= @pDesde AND momento < @pHasta");
                cmd.Parameters.AddWithValue("@pDesde", p0);
                cmd.Parameters.AddWithValue("@pHasta", p1);
            }
            if (!string.IsNullOrWhiteSpace(filtro.Texto))
            {
                // LIKE con comodines escapados: el texto del usuario nunca debe ampliar el patrón.
                var t = "%" + filtro.Texto.Trim().Replace("%", "\\%").Replace("_", "\\_") + "%";
                condiciones.Add("(paciente LIKE @t OR paciente_id LIKE @t OR acceso LIKE @t"
                                + " OR estudio_uid LIKE @t OR unidad LIKE @t OR mensaje LIKE @t)");
                cmd.Parameters.AddWithValue("@t", t);
            }
            cmd.CommandText = "SELECT id, momento, usuario, equipo, ip, ip_local, evento, resultado, lote,"
                + " paciente, paciente_id, acceso, estudio_uid, modalidad, fecha_estudio, imagines,"
                + " medio, unidad, mensaje FROM bitacora_quemador"
                + (condiciones.Count > 0 ? " WHERE " + string.Join(" AND ", condiciones) : "")
                + " ORDER BY momento DESC, id DESC LIMIT @lim";
            cmd.Parameters.AddWithValue("@lim", Math.Clamp(filtro.Limite <= 0 ? 500 : filtro.Limite, 1, 5000));
            var lista = new List<QuemadorEvento>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) lista.Add(Leer(r));
            return lista;
        }
        catch (Exception e) { Fallo("leer", e); return Array.Empty<QuemadorEvento>(); }
    }

    public override (IReadOnlyList<string> Usuarios, IReadOnlyList<string> Equipos) Valores()
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            return (Distintos(cn, "usuario"), Distintos(cn, "equipo"));
        }
        catch (Exception e)
        {
            Fallo("leer valores", e);
            return (Array.Empty<string>(), Array.Empty<string>());
        }
    }

    public override IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null)
    {
        var salida = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            // Igual que en la bitácora de accesos: se agrupa por día local, no por día UTC.
            cmd.Parameters.AddWithValue("@off", OffsetLocalParaSql());
            var sql = "SELECT DATE_FORMAT(CONVERT_TZ(momento, '+00:00', @off), '%Y-%m-%d') d, COUNT(*) n" +
                      " FROM bitacora_quemador";
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

    private static IReadOnlyList<string> Distintos(MySqlConnection cn, string columna)
    {
        using var cmd = cn.CreateCommand();
        // El nombre de columna viene del código, nunca del usuario.
        cmd.CommandText = $"SELECT DISTINCT `{columna}` FROM bitacora_quemador WHERE `{columna}` <> '' ORDER BY 1";
        var lista = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(r.GetString(0));
        return lista;
    }

    private static QuemadorEvento Leer(MySqlDataReader r) => new(
        r.GetInt64(0),
        DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc),
        r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
        r.GetString(8), r.GetString(9), r.GetString(10), r.GetString(11), r.GetString(12), r.GetString(13),
        r.GetString(14), r.GetInt32(15), r.GetString(16), r.GetString(17), r.GetString(18));
}
