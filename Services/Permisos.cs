using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Matriz de permisos por rol (lo que se edita en "Roles y permisos"). Si un rol no tiene matriz
/// guardada, se usan los permisos por defecto del catálogo (`CatalogoPermisos.PorDefecto`): todo
/// para `Admin`, el subconjunto de seguimiento para `Ingeniero` y **vacío** para roles nuevos.
public interface IPermissionStore
{
    /// Permisos activos de un rol (guardados o por defecto).
    IReadOnlySet<string> Activos(string rol);
    bool Tiene(string rol, string clave) => Activos(rol).Contains(clave);
    /// Guarda la matriz de un rol (solo claves válidas del catálogo).
    (int Status, string? Error) Guardar(string rol, IEnumerable<string> claves);
}

public abstract class PermissionStoreBase : IPermissionStore
{
    public abstract IReadOnlySet<string> Activos(string rol);
    public bool Tiene(string rol, string clave) => Activos(rol).Contains(clave);
    public abstract (int, string?) Guardar(string rol, IEnumerable<string> claves);

    /// Devuelve el nombre canónico del rol: `Admin`/`Ingeniero` se normalizan a su forma del
    /// sistema y cualquier otro rol personalizado se acepta tal cual (recortado). Cadena vacía si
    /// no se indicó rol.
    protected static string RolValido(string rol)
    {
        var r = (rol ?? "").Trim();
        if (r.Length == 0) return "";
        if (string.Equals(r, Roles.Admin, StringComparison.OrdinalIgnoreCase)) return Roles.Admin;
        if (string.Equals(r, Roles.Ingeniero, StringComparison.OrdinalIgnoreCase)) return Roles.Ingeniero;
        return r;
    }

    /// Versión del catálogo contra la que se guardaron las matrices (1 si nunca se guardó ninguna).
    protected static int VersionGuardada(IConfigStore config) =>
        int.TryParse(config.Get(ConfigKeys.PermisosVersion, "1"), out var v) && v > 0 ? v : 1;

    /// Una matriz guardada antes de la versión actual no conoce las claves nuevas: se completan con
    /// el valor por defecto del rol para que una actualización no deje a nadie sin las funciones
    /// nuevas (lo que el Admin haya desmarcado a mano se respeta, porque ya está en la lista).
    protected static IReadOnlySet<string> Completar(string rol, IEnumerable<string> guardadas, int versionGuardada)
    {
        var activas = new HashSet<string>(guardadas.Where(CatalogoPermisos.EsClaveValida), StringComparer.Ordinal);
        if (versionGuardada < CatalogoPermisos.Version)
            foreach (var clave in CatalogoPermisos.NuevasDesde(versionGuardada, rol)) activas.Add(clave);
        return activas;
    }

    /// Filtra claves desconocidas y devuelve el primer error o null.
    protected static string? Validar(string rol, IEnumerable<string> claves, out List<string> limpias)
    {
        limpias = claves.Where(CatalogoPermisos.EsClaveValida).Distinct(StringComparer.Ordinal).ToList();
        if (RolValido(rol) == "") return "Falta indicar el rol.";
        if (limpias.Count != claves.Distinct(StringComparer.Ordinal).Count()) return "Hay permisos desconocidos en la lista.";
        return null;
    }
}

/// Respaldo en archivo JSON: App_Data/permisos.json ({ "Admin": [...], "Ingeniero": [...] }).
public class JsonPermissionStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonPermissionStore> logger, IConfigStore cfg)
    : PermissionStoreBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:PermissionFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "permisos.json");

    private Dictionary<string, List<string>> Leer()
    {
        if (!File.Exists(_path)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path), Json) ?? new(); }
        catch (Exception e) { logger.LogWarning("No se pudo leer permisos.json: {Error}", e.Message); return new(); }
    }

    public override IReadOnlySet<string> Activos(string rol)
    {
        var r = RolValido(rol);
        if (r == "") return CatalogoPermisos.PorDefecto(rol);
        lock (_lock)
        {
            return Leer().TryGetValue(r, out var claves) && claves is { Count: > 0 }
                ? Completar(r, claves, VersionGuardada(cfg))
                : CatalogoPermisos.PorDefecto(r);
        }
    }

    public override (int, string?) Guardar(string rol, IEnumerable<string> claves)
    {
        var error = Validar(rol, claves, out var limpias);
        if (error is not null) return (400, error);
        lock (_lock)
        {
            var datos = Leer();
            datos[RolValido(rol)] = limpias;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(datos, Json));
            }
            catch (Exception e) { logger.LogWarning("No se pudo guardar permisos.json: {Error}", e.Message); return (500, "No se pudo guardar la matriz."); }
            cfg.Set(ConfigKeys.PermisosVersion, CatalogoPermisos.Version.ToString());
            return (204, null);
        }
    }
}

/// Matriz en MySQL (misma base de usuarios): tabla `permisos` (una fila por clave activa).
public class MySqlPermissionStore(IConfiguration config, IConfigStore cfg) : PermissionStoreBase
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
            CREATE TABLE IF NOT EXISTS `{db}`.`permisos` (
              `rol`   VARCHAR(20) NOT NULL,
              `clave` VARCHAR(60) NOT NULL,
              PRIMARY KEY (`rol`, `clave`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public override IReadOnlySet<string> Activos(string rol)
    {
        var r = RolValido(rol);
        if (r == "") return CatalogoPermisos.PorDefecto(rol);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT clave FROM permisos WHERE rol = @r";
            cmd.Parameters.AddWithValue("@r", r);
            var claves = new List<string>();
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) claves.Add(rd.GetString(0));
            return claves.Count == 0
                ? CatalogoPermisos.PorDefecto(r)
                : Completar(r, claves, VersionGuardada(cfg));
        }
        catch { return CatalogoPermisos.PorDefecto(r); }
    }

    public override (int, string?) Guardar(string rol, IEnumerable<string> claves)
    {
        var error = Validar(rol, claves, out var limpias);
        if (error is not null) return (400, error);
        var r = RolValido(rol);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var tx = cn.BeginTransaction();
            using (var del = cn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM permisos WHERE rol = @r";
                del.Parameters.AddWithValue("@r", r);
                del.ExecuteNonQuery();
            }
            foreach (var clave in limpias)
            {
                using var ins = cn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO permisos (rol, clave) VALUES (@r, @c)";
                ins.Parameters.AddWithValue("@r", r);
                ins.Parameters.AddWithValue("@c", clave);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
            cfg.Set(ConfigKeys.PermisosVersion, CatalogoPermisos.Version.ToString());
            return (204, null);
        }
        catch { return (500, "No se pudo guardar la matriz."); }
    }
}
