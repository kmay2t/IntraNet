using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Almacén de los tres inventarios del módulo Gestión (equipos, IPs y AETitle). Los tres comparten
/// tabla/archivo y se distinguen por `coleccion`; cada registro guarda su hospital y sus campos en
/// un diccionario (el esquema viene de `InventarioCatalogo`, así que ampliar un inventario no
/// requiere tocar el almacén). Las reglas de negocio y los permisos viven en `InventarioService`.
public interface IInventarioStore
{
    IReadOnlyList<ItemInventario> List(string coleccion);
    ItemInventario? Find(string coleccion, long id);
    /// <returns>201 creado (con Id), 400 datos inválidos.</returns>
    (int Status, string? Error, ItemInventario? Item) Create(ItemInventario borrador);
    /// <returns>200 guardado, 404 no existe.</returns>
    (int Status, string? Error, ItemInventario? Item) Guardar(ItemInventario item);
    /// <returns>204 eliminado, 404 no existe.</returns>
    (int Status, string? Error) Delete(string coleccion, long id);
}

public abstract class InventarioStoreBase : IInventarioStore
{
    public abstract IReadOnlyList<ItemInventario> List(string coleccion);
    public abstract ItemInventario? Find(string coleccion, long id);
    public abstract (int, string?, ItemInventario?) Create(ItemInventario borrador);
    public abstract (int, string?, ItemInventario?) Guardar(ItemInventario item);
    public abstract (int, string?) Delete(string coleccion, long id);

    /// Deja solo los campos del esquema, recortados a su largo máximo, con el hospital normalizado.
    protected static ItemInventario Limpiar(ItemInventario i)
    {
        var coleccion = InventarioCatalogo.PorClave(i.Coleccion);
        var datos = new Dictionary<string, string>(StringComparer.Ordinal);
        if (coleccion is not null)
        {
            foreach (var campo in coleccion.Campos)
            {
                var v = (i.Datos.TryGetValue(campo.Clave, out var x) ? x : "").Trim();
                if (v.Length > campo.Max) v = v[..campo.Max];
                datos[campo.Clave] = v;
            }
        }
        return i with
        {
            Coleccion = coleccion?.Clave ?? i.Coleccion,
            Hospital = Hospitales.Normalizar(i.Hospital, permiteGeneral: true),
            Datos = datos,
        };
    }

    protected static string? Validar(ItemInventario i)
    {
        var coleccion = InventarioCatalogo.PorClave(i.Coleccion);
        if (coleccion is null) return "El inventario no existe.";
        return InventarioRules.Validar(coleccion, i.Hospital, i.Datos);
    }
}

/// Almacén de respaldo en archivo JSON (App_Data/inventario.json), usado cuando MySQL no responde.
public class JsonInventarioStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonInventarioStore> logger)
    : InventarioStoreBase
{
    private sealed class Archivo
    {
        public long NextId { get; set; } = 1;
        public List<ItemInventario> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:InventarioFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "inventario.json");
    private Archivo _data = new();
    private bool _cargado = false;

    private Archivo Cargar()
    {
        if (!File.Exists(_path)) return new Archivo();
        try { return JsonSerializer.Deserialize<Archivo>(File.ReadAllText(_path), Json) ?? new Archivo(); }
        catch (Exception e) { logger.LogWarning("No se pudo leer inventario.json: {Error}", e.Message); return new Archivo(); }
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

    private static bool MismaColeccion(ItemInventario i, string coleccion) =>
        string.Equals(i.Coleccion, coleccion, StringComparison.OrdinalIgnoreCase);

    public override IReadOnlyList<ItemInventario> List(string coleccion)
    {
        lock (_lock)
        {
            return Datos().Items.Where(i => MismaColeccion(i, coleccion))
                .OrderBy(i => i.Hospital).ThenBy(i => i.Id).ToList();
        }
    }

    public override ItemInventario? Find(string coleccion, long id)
    {
        lock (_lock) { return Datos().Items.FirstOrDefault(i => i.Id == id && MismaColeccion(i, coleccion)); }
    }

    public override (int, string?, ItemInventario?) Create(ItemInventario borrador)
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

    public override (int, string?, ItemInventario?) Guardar(ItemInventario item)
    {
        if (Validar(item) is string error) return (400, error, null);
        lock (_lock)
        {
            var data = Datos();
            var i = data.Items.FindIndex(x => x.Id == item.Id && MismaColeccion(x, item.Coleccion));
            if (i < 0) return (404, "El registro no existe.", null);
            data.Items[i] = Limpiar(item) with { Id = data.Items[i].Id, Coleccion = data.Items[i].Coleccion };
            GuardarArchivo();
            return (200, null, data.Items[i]);
        }
    }

    public override (int, string?) Delete(string coleccion, long id)
    {
        lock (_lock)
        {
            var data = Datos();
            var n = data.Items.RemoveAll(i => i.Id == id && MismaColeccion(i, coleccion));
            if (n == 0) return (404, "El registro no existe.");
            GuardarArchivo();
            return (204, null);
        }
    }
}

/// Almacén en MySQL (misma base de usuarios): tabla `inventario`.
public class MySqlInventarioStore(IConfiguration config) : InventarioStoreBase
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
            CREATE TABLE IF NOT EXISTS `{db}`.`inventario` (
              `id`          BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `coleccion`   VARCHAR(20)  NOT NULL,
              `hospital`    VARCHAR(10)  NOT NULL DEFAULT '',
              `datos`       TEXT         NOT NULL,
              `creado_por`  VARCHAR(40)  NOT NULL DEFAULT '',
              `creado`      DATETIME(6)  NOT NULL,
              `actualizado` DATETIME(6)  NULL,
              KEY `idx_inventario_coleccion` (`coleccion`, `hospital`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ItemInventario Leer(MySqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2),
        JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(3), Json) ?? new(),
        r.GetString(4),
        DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Unspecified),
        r.IsDBNull(6) ? null : DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Unspecified));

    public override IReadOnlyList<ItemInventario> List(string coleccion)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT id, coleccion, hospital, datos, creado_por, creado, actualizado
            FROM inventario WHERE coleccion = @c ORDER BY hospital, id
            """;
        cmd.Parameters.AddWithValue("@c", coleccion);
        var lista = new List<ItemInventario>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Leer(r));
        return lista;
    }

    public override ItemInventario? Find(string coleccion, long id)
    {
        using var cn = Conexion();
        cn.Open();
        return FindCon(cn, coleccion, id);
    }

    public override (int, string?, ItemInventario?) Create(ItemInventario borrador)
    {
        if (Validar(borrador) is string error) return (400, error, null);
        var limpio = Limpiar(borrador);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO inventario (coleccion, hospital, datos, creado_por, creado, actualizado)
            VALUES (@c, @h, @d, @cp, @cr, NULL);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@c", limpio.Coleccion);
        cmd.Parameters.AddWithValue("@h", limpio.Hospital);
        cmd.Parameters.AddWithValue("@d", JsonSerializer.Serialize(limpio.Datos, Json));
        cmd.Parameters.AddWithValue("@cp", Recortar(limpio.CreadoPor, 40));
        cmd.Parameters.AddWithValue("@cr", limpio.Creado == default ? DateTime.Now : limpio.Creado);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        return (201, null, FindCon(cn, limpio.Coleccion, id));
    }

    public override (int, string?, ItemInventario?) Guardar(ItemInventario item)
    {
        if (Validar(item) is string error) return (400, error, null);
        var limpio = Limpiar(item);
        using var cn = Conexion();
        cn.Open();
        if (FindCon(cn, limpio.Coleccion, limpio.Id) is null) return (404, "El registro no existe.", null);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE inventario SET hospital = @h, datos = @d, creado_por = @cp, creado = @cr, actualizado = @ac
            WHERE id = @id AND coleccion = @c
            """;
        cmd.Parameters.AddWithValue("@h", limpio.Hospital);
        cmd.Parameters.AddWithValue("@d", JsonSerializer.Serialize(limpio.Datos, Json));
        cmd.Parameters.AddWithValue("@cp", Recortar(limpio.CreadoPor, 40));
        cmd.Parameters.AddWithValue("@cr", limpio.Creado);
        cmd.Parameters.AddWithValue("@ac", (object?)limpio.Actualizado ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", limpio.Id);
        cmd.Parameters.AddWithValue("@c", limpio.Coleccion);
        cmd.ExecuteNonQuery();
        return (200, null, FindCon(cn, limpio.Coleccion, limpio.Id));
    }

    public override (int, string?) Delete(string coleccion, long id)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM inventario WHERE id = @id AND coleccion = @c";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@c", coleccion);
        return cmd.ExecuteNonQuery() == 0 ? (404, "El registro no existe.") : (204, null);
    }

    private static string Recortar(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    private ItemInventario? FindCon(MySqlConnection cn, string coleccion, long id)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT id, coleccion, hospital, datos, creado_por, creado, actualizado
            FROM inventario WHERE id = @id AND coleccion = @c
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@c", coleccion);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Leer(r) : null;
    }
}
