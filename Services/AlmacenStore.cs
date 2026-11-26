using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Almacén de los movimientos del almacén de equipos (entradas, salidas y traslados). Cada
/// movimiento guarda su folio único, su estado y una fotografía de las líneas afectadas. Las
/// existencias NO viven aquí: se calculan y actualizan sobre el catálogo de equipos
/// (`IInventarioStore`, colección `equipos`), que es la única fuente de verdad del stock.
public interface IAlmacenStore
{
    IReadOnlyList<MovimientoInventario> List();
    MovimientoInventario? Find(long id);
    /// <returns>201 creado (con Id y Folio), 400 datos inválidos.</returns>
    (int Status, string? Error, MovimientoInventario? Item) Create(MovimientoInventario borrador);
    /// <returns>200 guardado, 404 no existe.</returns>
    (int Status, string? Error, MovimientoInventario? Item) Guardar(MovimientoInventario item);
    /// Siguiente número de folio para un tipo (los movimientos no se borran, así que es seguro).
    long SiguienteFolio(string tipo);
}

public abstract class AlmacenStoreBase : IAlmacenStore
{
    public abstract IReadOnlyList<MovimientoInventario> List();
    public abstract MovimientoInventario? Find(long id);
    public abstract (int, string?, MovimientoInventario?) Create(MovimientoInventario borrador);
    public abstract (int, string?, MovimientoInventario?) Guardar(MovimientoInventario item);
    public abstract long SiguienteFolio(string tipo);

    protected static string? Validar(MovimientoInventario m) => AlmacenRules.CabeceraError(m);

    /// Folio legible y único por tipo y año: ENT-2026-0001, SAL-2026-0001, TRA-2026-0001.
    public static string FolioPublico(string tipo, long n)
    {
        var pref = tipo switch
        {
            TiposMovimiento.Entrada => "ENT",
            TiposMovimiento.Salida => "SAL",
            _ => "TRA",
        };
        return $"{pref}-{DateTime.Now:yyyy}-{n:0000}";
    }

    protected static MovimientoInventario Limpiar(MovimientoInventario m) => m with
    {
        Motivo = Recortar(m.Motivo, 120),
        HospitalOrigen = Hospitales.Normalizar(m.HospitalOrigen, permiteGeneral: true),
        UbicacionOrigen = Recortar(m.UbicacionOrigen, 120),
        HospitalDestino = Hospitales.Normalizar(m.HospitalDestino, permiteGeneral: true),
        UbicacionDestino = Recortar(m.UbicacionDestino, 120),
        ResponsableEntrega = Recortar(m.ResponsableEntrega, 120),
        ResponsableRecibe = Recortar(m.ResponsableRecibe, 120),
        UsuarioRegistra = Recortar(m.UsuarioRegistra, 40),
        DocumentoReferencia = Recortar(m.DocumentoReferencia, 120),
        Observaciones = Recortar(m.Observaciones, 2000),
        UsuarioAutoriza = Recortar(m.UsuarioAutoriza, 40),
    };

    protected static string Recortar(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }
}

/// Respaldo en archivo JSON (App_Data/almacen.json), usado cuando MySQL no responde.
public class JsonAlmacenStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonAlmacenStore> logger)
    : AlmacenStoreBase
{
    private sealed class Archivo
    {
        public long NextId { get; set; } = 1;
        public List<MovimientoInventario> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:AlmacenFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "almacen.json");
    private Archivo _data = new();
    private bool _cargado = false;

    private Archivo Cargar()
    {
        if (!File.Exists(_path)) return new Archivo();
        try { return JsonSerializer.Deserialize<Archivo>(File.ReadAllText(_path), Json) ?? new Archivo(); }
        catch (Exception e) { logger.LogWarning("No se pudo leer almacen.json: {Error}", e.Message); return new Archivo(); }
    }

    private void GuardarArchivo()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_data, Json));
    }

    private Archivo Datos()
    {
        if (!_cargado) { _data = Cargar(); _cargado = true; }
        return _data;
    }

    public override IReadOnlyList<MovimientoInventario> List()
    {
        lock (_lock) { return Datos().Items.OrderByDescending(i => i.Fecha).ThenByDescending(i => i.Id).ToList(); }
    }

    public override MovimientoInventario? Find(long id)
    {
        lock (_lock) { return Datos().Items.FirstOrDefault(i => i.Id == id); }
    }

    public override (int, string?, MovimientoInventario?) Create(MovimientoInventario borrador)
    {
        if (Validar(borrador) is string error) return (400, error, null);
        lock (_lock)
        {
            var data = Datos();
            var item = Limpiar(borrador) with { Id = data.NextId++ };
            data.Items.Add(item);
            GuardarArchivo();
            return (201, null, item);
        }
    }

    public override (int, string?, MovimientoInventario?) Guardar(MovimientoInventario item)
    {
        lock (_lock)
        {
            var data = Datos();
            var i = data.Items.FindIndex(x => x.Id == item.Id);
            if (i < 0) return (404, "El movimiento no existe.", null);
            data.Items[i] = Limpiar(item) with { Id = data.Items[i].Id, Folio = data.Items[i].Folio };
            GuardarArchivo();
            return (200, null, data.Items[i]);
        }
    }

    public override long SiguienteFolio(string tipo)
    {
        lock (_lock) { return Datos().Items.Count(i => i.Tipo == tipo) + 1; }
    }
}

/// Almacén en MySQL (misma base de usuarios): tabla `almacen_movimientos`.
public class MySqlAlmacenStore(IConfiguration config) : AlmacenStoreBase
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
            CREATE TABLE IF NOT EXISTS `{db}`.`almacen_movimientos` (
              `id`                 BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `folio`              VARCHAR(30)  NOT NULL,
              `tipo`               VARCHAR(20)  NOT NULL,
              `motivo`             VARCHAR(120) NOT NULL DEFAULT '',
              `hospital_origen`    VARCHAR(10)  NOT NULL DEFAULT '',
              `ubicacion_origen`   VARCHAR(120) NOT NULL DEFAULT '',
              `hospital_destino`   VARCHAR(10)  NOT NULL DEFAULT '',
              `ubicacion_destino`  VARCHAR(120) NOT NULL DEFAULT '',
              `estado`             VARCHAR(20)  NOT NULL,
              `fecha`              DATETIME(6)  NOT NULL,
              `resp_entrega`       VARCHAR(120) NOT NULL DEFAULT '',
              `resp_recibe`        VARCHAR(120) NOT NULL DEFAULT '',
              `usuario_registra`   VARCHAR(40)  NOT NULL DEFAULT '',
              `doc_ref`            VARCHAR(120) NOT NULL DEFAULT '',
              `observaciones`      TEXT         NOT NULL,
              `creado`             DATETIME(6)  NOT NULL,
              `modificado`         DATETIME(6)  NULL,
              `fecha_autorizacion` DATETIME(6)  NULL,
              `usuario_autoriza`   VARCHAR(40)  NOT NULL DEFAULT '',
              `detalles`           TEXT         NOT NULL,
              UNIQUE KEY `uq_almacen_folio` (`folio`),
              KEY `idx_almacen_tipo_estado` (`tipo`, `estado`),
              KEY `idx_almacen_fecha` (`fecha`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static MovimientoInventario Leer(MySqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
        r.GetString(8),
        DateTime.SpecifyKind(r.GetDateTime(9), DateTimeKind.Unspecified),
        r.GetString(10), r.GetString(11), r.GetString(12), r.GetString(13), r.GetString(14),
        DateTime.SpecifyKind(r.GetDateTime(15), DateTimeKind.Unspecified),
        r.IsDBNull(16) ? null : DateTime.SpecifyKind(r.GetDateTime(16), DateTimeKind.Unspecified),
        r.IsDBNull(17) ? null : DateTime.SpecifyKind(r.GetDateTime(17), DateTimeKind.Unspecified),
        r.GetString(18),
        JsonSerializer.Deserialize<List<MovimientoDetalle>>(r.GetString(19), Json) ?? new());

    private const string Select = """
        SELECT id, folio, tipo, motivo, hospital_origen, ubicacion_origen, hospital_destino,
               ubicacion_destino, estado, fecha, resp_entrega, resp_recibe, usuario_registra,
               doc_ref, observaciones, creado, modificado, fecha_autorizacion, usuario_autoriza, detalles
        FROM almacen_movimientos
        """;

    public override IReadOnlyList<MovimientoInventario> List()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = Select + " ORDER BY fecha DESC, id DESC";
        var lista = new List<MovimientoInventario>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Leer(r));
        return lista;
    }

    public override MovimientoInventario? Find(long id)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = Select + " WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Leer(r) : null;
    }

    public override (int, string?, MovimientoInventario?) Create(MovimientoInventario borrador)
    {
        if (Validar(borrador) is string error) return (400, error, null);
        var limpio = Limpiar(borrador);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO almacen_movimientos
              (folio, tipo, motivo, hospital_origen, ubicacion_origen, hospital_destino, ubicacion_destino,
               estado, fecha, resp_entrega, resp_recibe, usuario_registra, doc_ref, observaciones,
               creado, modificado, fecha_autorizacion, usuario_autoriza, detalles)
            VALUES
              (@fo,@ti,@mo,@ho,@uo,@hd,@ud,@es,@fe,@re,@rr,@ur,@dr,@ob,@cr,NULL,@fa,@ua,@de);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, limpio);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        return (201, null, FindCon(cn, id));
    }

    public override (int, string?, MovimientoInventario?) Guardar(MovimientoInventario item)
    {
        var limpio = Limpiar(item);
        using var cn = Conexion();
        cn.Open();
        if (FindCon(cn, limpio.Id) is null) return (404, "El movimiento no existe.", null);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE almacen_movimientos SET
              motivo=@mo, hospital_origen=@ho, ubicacion_origen=@uo, hospital_destino=@hd,
              ubicacion_destino=@ud, estado=@es, fecha=@fe, resp_entrega=@re, resp_recibe=@rr,
              usuario_registra=@ur, doc_ref=@dr, observaciones=@ob, modificado=@mg,
              fecha_autorizacion=@fa, usuario_autoriza=@ua, detalles=@de
            WHERE id=@id
            """;
        AgregarParametros(cmd, limpio);
        cmd.Parameters.AddWithValue("@id", limpio.Id);
        cmd.Parameters.AddWithValue("@mg", (object?)limpio.Modificado ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return (200, null, FindCon(cn, limpio.Id));
    }

    public override long SiguienteFolio(string tipo)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) + 1 FROM almacen_movimientos WHERE tipo = @t";
        cmd.Parameters.AddWithValue("@t", tipo);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private void AgregarParametros(MySqlCommand cmd, MovimientoInventario m)
    {
        cmd.Parameters.AddWithValue("@fo", m.Folio);
        cmd.Parameters.AddWithValue("@ti", m.Tipo);
        cmd.Parameters.AddWithValue("@mo", m.Motivo);
        cmd.Parameters.AddWithValue("@ho", m.HospitalOrigen);
        cmd.Parameters.AddWithValue("@uo", m.UbicacionOrigen);
        cmd.Parameters.AddWithValue("@hd", m.HospitalDestino);
        cmd.Parameters.AddWithValue("@ud", m.UbicacionDestino);
        cmd.Parameters.AddWithValue("@es", m.Estado);
        cmd.Parameters.AddWithValue("@fe", m.Fecha);
        cmd.Parameters.AddWithValue("@re", m.ResponsableEntrega);
        cmd.Parameters.AddWithValue("@rr", m.ResponsableRecibe);
        cmd.Parameters.AddWithValue("@ur", m.UsuarioRegistra);
        cmd.Parameters.AddWithValue("@dr", m.DocumentoReferencia);
        cmd.Parameters.AddWithValue("@ob", m.Observaciones);
        cmd.Parameters.AddWithValue("@cr", m.Creado == default ? DateTime.Now : m.Creado);
        cmd.Parameters.AddWithValue("@fa", (object?)m.FechaAutorizacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ua", m.UsuarioAutoriza);
        cmd.Parameters.AddWithValue("@de", JsonSerializer.Serialize(m.Detalles, Json));
    }

    private MovimientoInventario? FindCon(MySqlConnection cn, long id)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = Select + " WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Leer(r) : null;
    }
}
