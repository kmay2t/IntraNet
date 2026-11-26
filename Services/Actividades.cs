using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Almacén de actividades/incidentes/tickets del módulo Gestión. Igual que los usuarios y la
/// bitácora, tiene dos implementaciones con la misma semántica (MySQL o JSON de respaldo) para
/// que la API y la interfaz no dependan de dónde se guarden.
/// Las reglas de negocio (transiciones de estado, asignación, reportes) viven en `GestionService`;
/// el almacén solo persiste: crea, guarda, busca, lista y elimina.
public interface IActivityStore
{
    IReadOnlyList<Actividad> List();
    Actividad? Find(long id);
    /// <returns>201 creada (con Id y, si es ticket, Numero asignados), 400 datos inválidos.</returns>
    (int Status, string? Error, Actividad? Item) Create(Actividad borrador);
    /// <returns>200 guardada, 404 no existe.</returns>
    (int Status, string? Error, Actividad? Item) Guardar(Actividad item);
    /// <returns>204 eliminada, 404 no existe.</returns>
    (int Status, string? Error) Delete(long id);
}

public abstract class ActivityStoreBase : IActivityStore
{
    public abstract IReadOnlyList<Actividad> List();
    public abstract Actividad? Find(long id);
    public abstract (int, string?, Actividad?) Create(Actividad borrador);
    public abstract (int, string?, Actividad?) Guardar(Actividad item);
    public abstract (int, string?) Delete(long id);

    /// Normaliza la fecha a medianoche local (el calendario agrupa por día).
    protected static DateTime Dia(DateTime fecha) => fecha.Date;

    protected static string Recortar(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    /// Valida los campos obligatorios de un borrador nuevo. Devuelve el primer error o null.
    protected static string? Validar(Actividad a) =>
        ActividadRules.TipoError(a.Tipo)
        ?? ActividadRules.HospitalError(a.Hospital)
        ?? ActividadRules.TituloError(a.Titulo)
        ?? ActividadRules.PrioridadError(a.Prioridad);

    /// Completa un item legado (datos guardados antes de existir tipo/prioridad/avance/hospital).
    /// El estado también se traduce: los registros de cuando había cinco estados llegan con su
    /// equivalente de hoy ("En proceso" → Pendiente, "Cerrada" → Completada).
    protected static Actividad Normalizar(Actividad a)
    {
        var estado = EstadosActividad.Normalizar(a.Estado);
        return a with
        {
            Tipo = TiposItem.EsValido(a.Tipo) ? a.Tipo : TiposItem.Actividad,
            Hospital = Hospitales.Normalizar(a.Hospital),
            Prioridad = Prioridades.EsValida(a.Prioridad) ? a.Prioridad : Prioridades.Media,
            Estado = EstadosActividad.EsValido(estado) ? estado : EstadosActividad.Pendiente,
            Avance = estado == EstadosActividad.Completada ? 100 : a.Avance,
        };
    }

    /// Siguiente número consecutivo de ticket a partir de los existentes.
    protected static long SiguienteNumero(IEnumerable<Actividad> items) =>
        items.Where(i => i.Tipo == TiposItem.Ticket).Select(i => i.Numero).DefaultIfEmpty(0).Max() + 1;
}

/// Almacén de respaldo en archivo JSON (App_Data/actividades.json). Se usa cuando MySQL no está
/// disponible. Los archivos de versiones anteriores (sin tipo/prioridad) se normalizan al cargar.
public class JsonActivityStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonActivityStore> logger)
    : ActivityStoreBase
{
    private sealed class Archivo
    {
        public long NextId { get; set; } = 1;
        public List<Actividad> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:ActivityFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "actividades.json");
    private Archivo _data = new();
    private bool _cargado = false;

    private Archivo Cargar()
    {
        if (!File.Exists(_path)) return new Archivo();
        try
        {
            var archivo = JsonSerializer.Deserialize<Archivo>(File.ReadAllText(_path), Json) ?? new Archivo();
            archivo.Items = archivo.Items.Select(Normalizar).ToList();
            return archivo;
        }
        catch (Exception e) { logger.LogWarning("No se pudo leer actividades.json: {Error}", e.Message); return new Archivo(); }
    }

    private void GuardarArchivo()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_data, Json));
    }

    /// Carga el archivo una sola vez; las escrituras recargan y guardan dentro del candado.
    private Archivo Datos()
    {
        if (!_cargado) { _data = Cargar(); _cargado = true; }
        return _data;
    }

    public override IReadOnlyList<Actividad> List()
    {
        lock (_lock) { return Datos().Items.OrderByDescending(a => a.Fecha).ThenBy(a => a.Id).ToList(); }
    }

    public override Actividad? Find(long id)
    {
        lock (_lock) { return Datos().Items.FirstOrDefault(a => a.Id == id); }
    }

    public override (int, string?, Actividad?) Create(Actividad borrador)
    {
        if (Validar(borrador) is string error) return (400, error, null);
        lock (_lock)
        {
            var data = Datos();
            var esTicket = borrador.Tipo == TiposItem.Ticket;
            var item = borrador with
            {
                Id = data.NextId++,
                Titulo = Recortar(borrador.Titulo.Trim(), 120),
                Descripcion = Recortar(borrador.Descripcion, 1000),
                Fecha = Dia(borrador.Fecha),
                FechaLimite = borrador.FechaLimite?.Date,
                Numero = esTicket ? SiguienteNumero(data.Items) : 0,
            };
            data.Items.Add(item);
            GuardarArchivo();
            return (201, null, item);
        }
    }

    public override (int, string?, Actividad?) Guardar(Actividad item)
    {
        lock (_lock)
        {
            var data = Datos();
            var i = data.Items.FindIndex(a => a.Id == item.Id);
            if (i < 0) return (404, "La actividad no existe.", null);
            data.Items[i] = Normalizar(item);
            GuardarArchivo();
            return (200, null, data.Items[i]);
        }
    }

    public override (int, string?) Delete(long id)
    {
        lock (_lock)
        {
            var data = Datos();
            var n = data.Items.RemoveAll(a => a.Id == id);
            if (n == 0) return (404, "La actividad no existe.");
            GuardarArchivo();
            return (204, null);
        }
    }
}

/// Almacén en MySQL (misma base de usuarios): tabla `actividades`. `EnsureSchema` crea la tabla
/// y, si ya existía de una versión anterior, agrega solo las columnas nuevas (nunca borra datos).
public class MySqlActivityStore(IConfiguration config) : ActivityStoreBase
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    private const string Columnas =
        "id, tipo, hospital, titulo, descripcion, usuario, fecha, fecha_limite, estado, prioridad, avance, " +
        "informe, justificacion, creado_por, creado, cerrado, numero, periodicidad_dias, asignado, ultimo_reporte, relacion_id";

    public void EnsureSchema()
    {
        var b = new MySqlConnectionStringBuilder(_cs);
        var db = b.Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`actividades` (
              `id`                BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `tipo`              VARCHAR(20)   NOT NULL DEFAULT 'Actividad',
              `hospital`          VARCHAR(10)   NOT NULL DEFAULT '',
              `titulo`            VARCHAR(120)  NOT NULL,
              `descripcion`       VARCHAR(1000) NOT NULL DEFAULT '',
              `usuario`           VARCHAR(40)   NOT NULL DEFAULT '',
              `fecha`             DATETIME      NOT NULL,
              `fecha_limite`      DATETIME      NULL,
              `estado`            VARCHAR(20)   NOT NULL DEFAULT 'Pendiente',
              `prioridad`         VARCHAR(20)   NOT NULL DEFAULT 'Media',
              `avance`            INT           NOT NULL DEFAULT 0,
              `informe`           VARCHAR(2000) NOT NULL DEFAULT '',
              `justificacion`     VARCHAR(500)  NOT NULL DEFAULT '',
              `creado_por`        VARCHAR(40)   NOT NULL DEFAULT '',
              `creado`            DATETIME(6)   NOT NULL,
              `cerrado`           DATETIME(6)   NULL,
              `numero`            BIGINT        NOT NULL DEFAULT 0,
              `periodicidad_dias` INT           NOT NULL DEFAULT 0,
              `asignado`          DATETIME(6)   NULL,
              `ultimo_reporte`    DATETIME(6)   NULL,
              `relacion_id`       BIGINT        NULL,
              KEY `idx_actividades_usuario_fecha` (`usuario`, `fecha`),
              KEY `idx_actividades_tipo_numero` (`tipo`, `numero`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();

        // Tablas creadas antes del módulo de tickets: agregar solo las columnas que falten.
        foreach (var (columna, ddl) in new[]
        {
            ("tipo",              "ADD COLUMN `tipo` VARCHAR(20) NOT NULL DEFAULT 'Actividad' AFTER `id`"),
            ("hospital",          "ADD COLUMN `hospital` VARCHAR(10) NOT NULL DEFAULT '' AFTER `tipo`"),
            ("fecha_limite",      "ADD COLUMN `fecha_limite` DATETIME NULL AFTER `fecha`"),
            ("prioridad",         "ADD COLUMN `prioridad` VARCHAR(20) NOT NULL DEFAULT 'Media' AFTER `estado`"),
            ("avance",            "ADD COLUMN `avance` INT NOT NULL DEFAULT 0 AFTER `prioridad`"),
            ("numero",            "ADD COLUMN `numero` BIGINT NOT NULL DEFAULT 0 AFTER `cerrado`"),
            ("periodicidad_dias", "ADD COLUMN `periodicidad_dias` INT NOT NULL DEFAULT 0 AFTER `numero`"),
            ("asignado",          "ADD COLUMN `asignado` DATETIME(6) NULL AFTER `periodicidad_dias`"),
            ("ultimo_reporte",    "ADD COLUMN `ultimo_reporte` DATETIME(6) NULL AFTER `asignado`"),
            ("relacion_id",       "ADD COLUMN `relacion_id` BIGINT NULL AFTER `ultimo_reporte`"),
        })
        {
            if (ColumnaExiste(cn, db, "actividades", columna)) continue;
            using var alter = cn.CreateCommand();
            alter.CommandText = $"ALTER TABLE `{db}`.`actividades` {ddl}";
            alter.ExecuteNonQuery();
        }
    }

    private static bool ColumnaExiste(MySqlConnection cn, string db, string tabla, string columna)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @t AND COLUMN_NAME = @c
            """;
        cmd.Parameters.AddWithValue("@db", db);
        cmd.Parameters.AddWithValue("@t", tabla);
        cmd.Parameters.AddWithValue("@c", columna);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static DateTime? FechaNula(MySqlDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Unspecified);

    private static Actividad Leer(MySqlDataReader r) => Normalizar(new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
        DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Unspecified),
        FechaNula(r, 7), r.GetString(8), r.GetString(9), r.GetInt32(10),
        r.GetString(11), r.GetString(12), r.GetString(13),
        DateTime.SpecifyKind(r.GetDateTime(14), DateTimeKind.Unspecified),
        FechaNula(r, 15), r.GetInt64(16), r.GetInt32(17),
        FechaNula(r, 18), FechaNula(r, 19),
        r.IsDBNull(20) ? null : r.GetInt64(20)));

    public override IReadOnlyList<Actividad> List()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM actividades ORDER BY fecha DESC, id DESC";
        var lista = new List<Actividad>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Leer(r));
        return lista;
    }

    public override Actividad? Find(long id)
    {
        using var cn = Conexion();
        cn.Open();
        return FindCon(cn, id);
    }

    public override (int, string?, Actividad?) Create(Actividad borrador)
    {
        if (Validar(borrador) is string error) return (400, error, null);
        using var cn = Conexion();
        cn.Open();
        long numero = 0;
        if (borrador.Tipo == TiposItem.Ticket)
        {
            using var max = cn.CreateCommand();
            max.CommandText = "SELECT COALESCE(MAX(numero), 0) + 1 FROM actividades WHERE tipo = 'Ticket'";
            numero = Convert.ToInt64(max.ExecuteScalar());
        }
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO actividades (tipo, hospital, titulo, descripcion, usuario, fecha, fecha_limite, estado, prioridad, avance,
                informe, justificacion, creado_por, creado, numero, periodicidad_dias, asignado, ultimo_reporte, relacion_id)
            VALUES (@tipo, @h, @t, @d, @u, @f, @fl, @e, @p, @av, '', '', @cp, @c, @n, @pd, @as, NULL, @rel);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@tipo", borrador.Tipo);
        cmd.Parameters.AddWithValue("@h", Recortar(Hospitales.Normalizar(borrador.Hospital), 10));
        cmd.Parameters.AddWithValue("@t", Recortar(borrador.Titulo.Trim(), 120));
        cmd.Parameters.AddWithValue("@d", Recortar(borrador.Descripcion, 1000));
        cmd.Parameters.AddWithValue("@u", Recortar(borrador.Usuario.Trim(), 40));
        cmd.Parameters.AddWithValue("@f", Dia(borrador.Fecha));
        cmd.Parameters.AddWithValue("@fl", (object?)borrador.FechaLimite?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", EstadosActividad.EsValido(borrador.Estado) ? borrador.Estado : EstadosActividad.Pendiente);
        cmd.Parameters.AddWithValue("@p", borrador.Prioridad);
        cmd.Parameters.AddWithValue("@av", borrador.Avance);
        cmd.Parameters.AddWithValue("@cp", Recortar(borrador.CreadoPor, 40));
        cmd.Parameters.AddWithValue("@c", borrador.Creado == default ? DateTime.Now : borrador.Creado);
        cmd.Parameters.AddWithValue("@n", numero);
        cmd.Parameters.AddWithValue("@pd", borrador.PeriodicidadDias);
        cmd.Parameters.AddWithValue("@as", (object?)borrador.Asignado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rel", (object?)borrador.RelacionId ?? DBNull.Value);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        return (201, null, FindCon(cn, id));
    }

    public override (int, string?, Actividad?) Guardar(Actividad item)
    {
        using var cn = Conexion();
        cn.Open();
        if (FindCon(cn, item.Id) is null) return (404, "La actividad no existe.", null);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE actividades SET tipo = @tipo, hospital = @h, titulo = @t, descripcion = @d, usuario = @u, fecha = @f,
              fecha_limite = @fl, estado = @e, prioridad = @p, avance = @av, informe = @inf, justificacion = @jus,
              creado_por = @cp, creado = @c, cerrado = @ce, numero = @n, periodicidad_dias = @pd,
              asignado = @as, ultimo_reporte = @ur, relacion_id = @rel
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@tipo", item.Tipo);
        cmd.Parameters.AddWithValue("@h", Recortar(Hospitales.Normalizar(item.Hospital), 10));
        cmd.Parameters.AddWithValue("@t", Recortar(item.Titulo.Trim(), 120));
        cmd.Parameters.AddWithValue("@d", Recortar(item.Descripcion, 1000));
        cmd.Parameters.AddWithValue("@u", Recortar(item.Usuario?.Trim() ?? "", 40));
        cmd.Parameters.AddWithValue("@f", Dia(item.Fecha));
        cmd.Parameters.AddWithValue("@fl", (object?)item.FechaLimite?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", item.Estado);
        cmd.Parameters.AddWithValue("@p", item.Prioridad);
        cmd.Parameters.AddWithValue("@av", item.Avance);
        cmd.Parameters.AddWithValue("@inf", Recortar(item.Informe, 2000));
        cmd.Parameters.AddWithValue("@jus", Recortar(item.Justificacion, 500));
        cmd.Parameters.AddWithValue("@cp", Recortar(item.CreadoPor, 40));
        cmd.Parameters.AddWithValue("@c", item.Creado);
        cmd.Parameters.AddWithValue("@ce", (object?)item.Cerrado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", item.Numero);
        cmd.Parameters.AddWithValue("@pd", item.PeriodicidadDias);
        cmd.Parameters.AddWithValue("@as", (object?)item.Asignado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ur", (object?)item.UltimoReporte ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rel", (object?)item.RelacionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", item.Id);
        cmd.ExecuteNonQuery();
        return (200, null, FindCon(cn, item.Id));
    }

    public override (int, string?) Delete(long id)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM actividades WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() == 0 ? (404, "La actividad no existe.") : (204, null);
    }

    private Actividad? FindCon(MySqlConnection cn, long id)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM actividades WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Leer(r) : null;
    }
}
