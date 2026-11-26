using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Un aviso publicado en el panel *Avisos* de la página de inicio. Lo ven **todos** los usuarios con
/// sesión; solo quien tiene el permiso `avisos.administrar` puede crearlo, editarlo o eliminarlo.
/// `Etiqueta` es el texto corto en mayúsculas que encabeza cada aviso (p. ej. "Esta semana");
/// `Activo = false` lo oculta del panel sin borrarlo (borrador / aviso pausado).
public record Aviso(
    int Id,
    string Titulo,
    string Cuerpo,
    string Etiqueta,
    bool Activo,
    DateTime Creado,
    string CreadoPor,
    DateTime Actualizado);

/// Reglas de los campos de un aviso (compartidas por la API y los almacenes).
public static class ReglasAviso
{
    public const int TituloMin = 3;
    public const int TituloMax = 120;
    public const int CuerpoMax = 500;
    public const int EtiquetaMax = 60;

    /// Mensaje de error o null si los campos son aceptables.
    public static string? Validar(string? titulo, string? cuerpo, string? etiqueta)
    {
        var t = (titulo ?? "").Trim();
        if (t.Length < TituloMin || t.Length > TituloMax)
            return $"El título del aviso debe tener entre {TituloMin} y {TituloMax} caracteres.";
        if ((cuerpo ?? "").Trim().Length > CuerpoMax)
            return $"El texto del aviso no puede pasar de {CuerpoMax} caracteres.";
        if ((etiqueta ?? "").Trim().Length > EtiquetaMax)
            return $"La etiqueta no puede pasar de {EtiquetaMax} caracteres.";
        return null;
    }
}

/// Almacén de avisos del inicio. `List()` devuelve **todos** (activos y pausados) ordenados del más
/// nuevo al más antiguo; la API ya filtra cuáles mostrar según el permiso de quien consulta.
public interface IAvisoStore
{
    IReadOnlyList<Aviso> List();
    (int Status, string? Error) Crear(string titulo, string cuerpo, string etiqueta, bool activo, string autor);
    (int Status, string? Error) Actualizar(int id, string titulo, string cuerpo, string etiqueta, bool activo);
    (int Status, string? Error) Eliminar(int id);
}

// ============================================================================
//  Implementación en MySQL
// ============================================================================

public class MySqlAvisoStore(IConfiguration config) : IAvisoStore
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    /// Crea la tabla `avisos` si no existe (primer arranque tras la actualización).
    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`avisos` (
              `id`          INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `titulo`      VARCHAR(120) NOT NULL,
              `cuerpo`      VARCHAR(500) NOT NULL DEFAULT '',
              `etiqueta`    VARCHAR(60)  NOT NULL DEFAULT '',
              `activo`      TINYINT(1)   NOT NULL DEFAULT 1,
              `creado`      DATETIME(6)  NOT NULL,
              `creadoPor`   VARCHAR(40)  NOT NULL DEFAULT '',
              `actualizado` DATETIME(6)  NOT NULL
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Aviso> List()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id, titulo, cuerpo, etiqueta, activo, creado, creadoPor, actualizado FROM avisos ORDER BY creado DESC, id DESC";
        using var r = cmd.ExecuteReader();
        var lista = new List<Aviso>();
        while (r.Read())
            lista.Add(new Aviso(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetBoolean(4), r.GetDateTime(5), r.GetString(6), r.GetDateTime(7)));
        return lista;
    }

    public (int Status, string? Error) Crear(string titulo, string cuerpo, string etiqueta, bool activo, string autor)
    {
        if (ReglasAviso.Validar(titulo, cuerpo, etiqueta) is { } e) return (400, e);
        var ahora = DateTime.UtcNow;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO avisos (titulo, cuerpo, etiqueta, activo, creado, creadoPor, actualizado)
            VALUES (@t, @c, @e, @a, @cre, @por, @act);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@t", (titulo ?? "").Trim());
        cmd.Parameters.AddWithValue("@c", (cuerpo ?? "").Trim());
        cmd.Parameters.AddWithValue("@e", (etiqueta ?? "").Trim());
        cmd.Parameters.AddWithValue("@a", activo);
        cmd.Parameters.AddWithValue("@cre", ahora);
        cmd.Parameters.AddWithValue("@por", autor ?? "");
        cmd.Parameters.AddWithValue("@act", ahora);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return (201, null);
    }

    public (int Status, string? Error) Actualizar(int id, string titulo, string cuerpo, string etiqueta, bool activo)
    {
        if (ReglasAviso.Validar(titulo, cuerpo, etiqueta) is { } e) return (400, e);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE avisos SET titulo=@t, cuerpo=@c, etiqueta=@e, activo=@a, actualizado=@act WHERE id=@id";
        cmd.Parameters.AddWithValue("@t", (titulo ?? "").Trim());
        cmd.Parameters.AddWithValue("@c", (cuerpo ?? "").Trim());
        cmd.Parameters.AddWithValue("@e", (etiqueta ?? "").Trim());
        cmd.Parameters.AddWithValue("@a", activo);
        cmd.Parameters.AddWithValue("@act", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0 ? (204, null) : (404, "El aviso no existe.");
    }

    public (int Status, string? Error) Eliminar(int id)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM avisos WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0 ? (204, null) : (404, "El aviso no existe.");
    }
}

// ============================================================================
//  Implementación en JSON (respaldo cuando MySQL no responde)
// ============================================================================

public class JsonAvisoStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonAvisoStore> logger) : IAvisoStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:AvisoFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "avisos.json");

    private List<Aviso> Leer()
    {
        if (!File.Exists(_path)) return new List<Aviso>();
        try
        {
            return JsonSerializer.Deserialize<List<Aviso>>(File.ReadAllText(_path), Json) ?? new List<Aviso>();
        }
        catch (Exception e) { logger.LogWarning("No se pudo leer avisos.json: {Error}", e.Message); return new List<Aviso>(); }
    }

    private void Escribir(List<Aviso> lista)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(lista, Json));
    }

    public IReadOnlyList<Aviso> List() { lock (_lock) return Leer().OrderByDescending(a => a.Creado).ThenByDescending(a => a.Id).ToList(); }

    public (int Status, string? Error) Crear(string titulo, string cuerpo, string etiqueta, bool activo, string autor)
    {
        if (ReglasAviso.Validar(titulo, cuerpo, etiqueta) is { } e) return (400, e);
        lock (_lock)
        {
            var lista = Leer();
            var id = lista.Count == 0 ? 1 : lista.Max(a => a.Id) + 1;
            var ahora = DateTime.UtcNow;
            lista.Add(new Aviso(id, (titulo ?? "").Trim(), (cuerpo ?? "").Trim(), (etiqueta ?? "").Trim(), activo, ahora, autor ?? "", ahora));
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar avisos.json: {Error}", ex.Message); return (500, "No se pudo crear el aviso."); }
            return (201, null);
        }
    }

    public (int Status, string? Error) Actualizar(int id, string titulo, string cuerpo, string etiqueta, bool activo)
    {
        if (ReglasAviso.Validar(titulo, cuerpo, etiqueta) is { } e) return (400, e);
        lock (_lock)
        {
            var lista = Leer();
            var i = lista.FindIndex(a => a.Id == id);
            if (i < 0) return (404, "El aviso no existe.");
            lista[i] = lista[i] with { Titulo = (titulo ?? "").Trim(), Cuerpo = (cuerpo ?? "").Trim(), Etiqueta = (etiqueta ?? "").Trim(), Activo = activo, Actualizado = DateTime.UtcNow };
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar avisos.json: {Error}", ex.Message); return (500, "No se pudo guardar el aviso."); }
            return (204, null);
        }
    }

    public (int Status, string? Error) Eliminar(int id)
    {
        lock (_lock)
        {
            var lista = Leer();
            var antes = lista.Count;
            lista.RemoveAll(a => a.Id == id);
            if (lista.Count == antes) return (404, "El aviso no existe.");
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar avisos.json: {Error}", ex.Message); return (500, "No se pudo eliminar el aviso."); }
            return (204, null);
        }
    }
}
