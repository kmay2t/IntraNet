using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Almacén de los metadatos del Playbook (documentación técnica en PDF). El archivo PDF vive en
/// disco, aquí solo sus datos y la ruta relativa. Las reglas y los permisos están en `PlaybookService`.
public interface IDocumentoStore
{
    IReadOnlyList<Documento> List();
    Documento? Find(long id);
    /// <returns>201 creado (con Id), 400 datos inválidos.</returns>
    (int Status, string? Error, Documento? Item) Create(Documento borrador);
    /// <returns>200 guardado, 404 no existe.</returns>
    (int Status, string? Error, Documento? Item) Guardar(Documento item);
    /// <returns>204 eliminado, 404 no existe.</returns>
    (int Status, string? Error) Delete(long id);
}

public abstract class DocumentoStoreBase : IDocumentoStore
{
    public abstract IReadOnlyList<Documento> List();
    public abstract Documento? Find(long id);
    public abstract (int, string?, Documento?) Create(Documento borrador);
    public abstract (int, string?, Documento?) Guardar(Documento item);
    public abstract (int, string?) Delete(long id);

    protected static string? Validar(Documento d) =>
        DocumentoRules.NombreError(d.Nombre)
        ?? DocumentoRules.VersionError(d.Version)
        ?? Hospitales.Error(d.Hospital, permiteGeneral: true)
        ?? DocumentoRules.EstadoError(d.Estado)
        ?? DocumentoRules.TipoError(d.Tipo);

    /// Deja los textos con su largo máximo y el hospital normalizado (admite "General").
    protected static Documento Limpiar(Documento d) => d with
    {
        Nombre = Recortar(d.Nombre, 150),
        NombreArchivo = Recortar(d.NombreArchivo, 200),
        Version = Recortar(d.Version, 20),
        Hospital = Hospitales.Normalizar(d.Hospital, permiteGeneral: true),
        Estado = EstadosDocumento.EsValido(d.Estado) ? d.Estado : EstadosDocumento.Vigente,
        Tipo = TiposDocumento.EsValido(d.Tipo) ? d.Tipo! : TiposDocumento.Otro,
    };

    protected static string Recortar(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }
}

/// Almacén de respaldo en archivo JSON (App_Data/playbook.json), usado cuando MySQL no responde.
public class JsonDocumentoStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonDocumentoStore> logger)
    : DocumentoStoreBase
{
    private sealed class Archivo
    {
        public long NextId { get; set; } = 1;
        public List<Documento> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:PlaybookFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "playbook.json");
    private Archivo _data = new();
    private bool _cargado = false;

    private Archivo Cargar()
    {
        if (!File.Exists(_path)) return new Archivo();
        try { return JsonSerializer.Deserialize<Archivo>(File.ReadAllText(_path), Json) ?? new Archivo(); }
        catch (Exception e) { logger.LogWarning("No se pudo leer playbook.json: {Error}", e.Message); return new Archivo(); }
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

    public override IReadOnlyList<Documento> List()
    {
        lock (_lock) { return Datos().Items.OrderBy(d => d.Nombre).ThenBy(d => d.Id).ToList(); }
    }

    public override Documento? Find(long id)
    {
        lock (_lock) { return Datos().Items.FirstOrDefault(d => d.Id == id); }
    }

    public override (int, string?, Documento?) Create(Documento borrador)
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

    public override (int, string?, Documento?) Guardar(Documento item)
    {
        if (Validar(item) is string error) return (400, error, null);
        lock (_lock)
        {
            var data = Datos();
            var i = data.Items.FindIndex(d => d.Id == item.Id);
            if (i < 0) return (404, "El documento no existe.", null);
            data.Items[i] = Limpiar(item) with { Id = data.Items[i].Id };
            GuardarArchivo();
            return (200, null, data.Items[i]);
        }
    }

    public override (int, string?) Delete(long id)
    {
        lock (_lock)
        {
            var data = Datos();
            if (data.Items.RemoveAll(d => d.Id == id) == 0) return (404, "El documento no existe.");
            GuardarArchivo();
            return (204, null);
        }
    }
}

/// Almacén en MySQL (misma base de usuarios): tabla `playbook`.
public class MySqlDocumentoStore(IConfiguration config) : DocumentoStoreBase
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    private const string Columnas =
        "id, nombre, nombre_archivo, version, hospital, fecha_publicacion, fecha_actualizacion, " +
        "subido_por, subido, tamano, ruta, estado, tipo";

    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`playbook` (
              `id`                  BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `nombre`              VARCHAR(150) NOT NULL,
              `nombre_archivo`      VARCHAR(200) NOT NULL DEFAULT '',
              `version`             VARCHAR(20)  NOT NULL DEFAULT 'v1.0',
              `hospital`            VARCHAR(10)  NOT NULL DEFAULT 'General',
              `fecha_publicacion`   DATETIME     NULL,
              `fecha_actualizacion` DATETIME     NULL,
              `subido_por`          VARCHAR(40)  NOT NULL DEFAULT '',
              `subido`              DATETIME(6)  NOT NULL,
              `tamano`              BIGINT       NOT NULL DEFAULT 0,
              `ruta`                VARCHAR(300) NOT NULL DEFAULT '',
              `estado`              VARCHAR(20)  NOT NULL DEFAULT 'Vigente',
              KEY `idx_playbook_hospital` (`hospital`, `estado`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();

        // Migración: bases creadas antes del campo `tipo` lo reciben sin perder datos.
        using var chk = cn.CreateCommand();
        chk.CommandText = """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'playbook' AND column_name = 'tipo';
            """;
        if (Convert.ToInt64(chk.ExecuteScalar()) == 0)
        {
            using var alt = cn.CreateCommand();
            alt.CommandText = "ALTER TABLE playbook ADD COLUMN `tipo` VARCHAR(30) NOT NULL DEFAULT 'Otro';";
            alt.ExecuteNonQuery();
        }
    }

    private static DateTime? FechaNula(MySqlDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Unspecified);

    private static Documento Leer(MySqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
        FechaNula(r, 5), FechaNula(r, 6), r.GetString(7),
        DateTime.SpecifyKind(r.GetDateTime(8), DateTimeKind.Unspecified),
        r.GetInt64(9), r.GetString(10), r.GetString(11), r.GetString(12));

    public override IReadOnlyList<Documento> List()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM playbook ORDER BY nombre, id";
        var lista = new List<Documento>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Leer(r));
        return lista;
    }

    public override Documento? Find(long id)
    {
        using var cn = Conexion();
        cn.Open();
        return FindCon(cn, id);
    }

    public override (int, string?, Documento?) Create(Documento borrador)
    {
        if (Validar(borrador) is string error) return (400, error, null);
        var d = Limpiar(borrador);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO playbook (nombre, nombre_archivo, version, hospital, fecha_publicacion, fecha_actualizacion,
                subido_por, subido, tamano, ruta, estado, tipo)
            VALUES (@n, @na, @v, @h, @fp, @fa, @sp, @s, @t, @r, @e, @tp);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, d);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        return (201, null, FindCon(cn, id));
    }

    public override (int, string?, Documento?) Guardar(Documento item)
    {
        if (Validar(item) is string error) return (400, error, null);
        var d = Limpiar(item);
        using var cn = Conexion();
        cn.Open();
        if (FindCon(cn, d.Id) is null) return (404, "El documento no existe.", null);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE playbook SET nombre = @n, nombre_archivo = @na, version = @v, hospital = @h,
              fecha_publicacion = @fp, fecha_actualizacion = @fa, subido_por = @sp, subido = @s,
              tamano = @t, ruta = @r, estado = @e, tipo = @tp
            WHERE id = @id
            """;
        AgregarParametros(cmd, d);
        cmd.Parameters.AddWithValue("@id", d.Id);
        cmd.ExecuteNonQuery();
        return (200, null, FindCon(cn, d.Id));
    }

    private static void AgregarParametros(MySqlCommand cmd, Documento d)
    {
        cmd.Parameters.AddWithValue("@n", d.Nombre);
        cmd.Parameters.AddWithValue("@na", d.NombreArchivo);
        cmd.Parameters.AddWithValue("@v", d.Version);
        cmd.Parameters.AddWithValue("@h", d.Hospital);
        cmd.Parameters.AddWithValue("@fp", (object?)d.FechaPublicacion?.Date ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fa", (object?)d.FechaActualizacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sp", d.SubidoPor);
        cmd.Parameters.AddWithValue("@s", d.Subido == default ? DateTime.Now : d.Subido);
        cmd.Parameters.AddWithValue("@t", d.Tamano);
        cmd.Parameters.AddWithValue("@r", d.Ruta);
        cmd.Parameters.AddWithValue("@e", d.Estado);
        cmd.Parameters.AddWithValue("@tp", TiposDocumento.EsValido(d.Tipo) ? d.Tipo : TiposDocumento.Otro);
    }

    public override (int, string?) Delete(long id)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM playbook WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() == 0 ? (404, "El documento no existe.") : (204, null);
    }

    private Documento? FindCon(MySqlConnection cn, long id)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM playbook WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Leer(r) : null;
    }
}
