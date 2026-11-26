using System.Text.Json;
using System.Text.RegularExpressions;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Un rol con nombre libre. Los roles de sistema (`Admin`, `Ingeniero`) vienen sembrados y no se
/// pueden eliminar ni renombrar. Solo el superusuario root conserva siempre todos los permisos.
public record RolInfo(string Nombre, bool EsSistema, DateTime Creado);

/// Almacén de roles dinámicos (crear / renombrar / eliminar). Renombrar reasigna los usuarios y la
/// matriz de permisos al nuevo nombre para no dejar referencias huérfanas.
public interface IRoleStore
{
    IReadOnlyList<RolInfo> List();
    bool Existe(string nombre);
    (int Status, string? Error) Crear(string nombre);
    (int Status, string? Error) Renombrar(string viejo, string nuevo);
    (int Status, string? Error) Eliminar(string nombre);
}

/// Ajustes finos por usuario: permisos concedidos (+) o revocados (−) que se aplican **encima** de
/// la matriz del rol. Es el modelo "rol como plantilla + excepciones por persona".
public interface IUserPermissionStore
{
    /// clave → true (concedido) / false (revocado). Solo claves válidas del catálogo.
    IReadOnlyDictionary<string, bool> Overrides(string usuario);
    /// Sustituye todos los ajustes del usuario por la lista dada (lista vacía = sin ajustes).
    (int Status, string? Error) Guardar(string usuario, IEnumerable<(string Clave, bool Concedido)> ajustes);
}

/// Resuelve permisos efectivos: root con rol Admin tiene todo; los demás usan la matriz del rol
/// con los ajustes por usuario aplicados encima (concedidos suman, revocados restan).
public interface IPermissionResolver
{
    IReadOnlySet<string> Efectivos(string usuario, string rol);
    /// Vacía la caché de permisos efectivos. Se llama tras guardar la matriz de un rol, los ajustes
    /// de un usuario, renombrar/eliminar un rol o cambiar el rol de un usuario, para que el cambio
    /// aplique de inmediato en las sesiones abiertas.
    void Invalidar();
}

public static partial class ReglasRol
{
    public const int LargoMin = 2;
    public const int LargoMax = 20;

    public static string Normalizar(string? nombre) => (nombre ?? "").Trim();

    /// Mensaje de error o null si el nombre es aceptable para un rol nuevo.
    public static string? Validar(string nombre, IRoleStore roles)
    {
        if (nombre.Length < LargoMin || nombre.Length > LargoMax)
            return $"El nombre del rol debe tener entre {LargoMin} y {LargoMax} caracteres.";
        if (!EsNombreValido(nombre))
            return "El nombre solo puede usar letras, números, espacio, punto, guion y guion bajo.";
        if (Roles.EsSistema(nombre))
            return "Ese nombre está reservado por el sistema.";
        if (roles.Existe(nombre))
            return "Ya existe un rol con ese nombre.";
        return null;
    }

    /// True si el nombre tiene la forma permitida (sin comprobar longitud ni duplicados).
    public static bool EsNombreValido(string nombre) => NombreValido().IsMatch(nombre);

    [GeneratedRegex(@"^[A-Za-z0-9 ._\-ÁÉÍÓÚÜÑáéíóúüñ]{2,20}$")]
    private static partial Regex NombreValido();
}

/// Resuelve permisos efectivos combinando la matriz del rol y los ajustes por usuario.
/// Resuelve permisos efectivos combinando la matriz del rol y los ajustes por usuario. Cachea el
/// resultado por usuario (30 s) para no consultar la base en cada petición de recurso estático; las
/// operaciones de escritura llaman a `Invalidar()` para que los cambios apliquen al momento.
public class PermissionResolver(IPermissionStore permisos, IUserPermissionStore overrides) : IPermissionResolver
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Cuando, IReadOnlySet<string> Set)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Efectivos(string usuario, string rol)
    {
        var u = usuario ?? "";
        var r = rol ?? "";
        var key = u + "\u0001" + r;
        if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.Cuando < Ttl)
            return hit.Set;
        var set = Calcular(u, r);
        _cache[key] = (DateTime.UtcNow, set);
        return set;
    }

    private IReadOnlySet<string> Calcular(string usuario, string rol)
    {
        rol = (rol ?? "").Trim();
        // Solo la cuenta root autenticada con rol Admin es superusuario. Conserva todo el
        // catálogo, incluidos permisos futuros, independientemente de matrices o excepciones.
        if (UserRules.IsRoot(usuario) && string.Equals(rol, Roles.Admin, StringComparison.OrdinalIgnoreCase))
            return new HashSet<string>(CatalogoPermisos.TodasLasClaves, StringComparer.Ordinal);
        var efectivos = new HashSet<string>(permisos.Activos(rol), StringComparer.Ordinal);

        // Ajustes por usuario encima de la matriz del rol: los concedidos suman, los revocados restan.
        foreach (var (clave, concedido) in overrides.Overrides(usuario))
        {
            if (!CatalogoPermisos.EsClaveValida(clave)) continue;
            if (concedido) efectivos.Add(clave); else efectivos.Remove(clave);
        }

        // Anti-bloqueo: quien tenga el rol Administrador nunca pierde el acceso a "Roles y permisos",
        // aunque un ajuste por usuario intente quitárselo.
        if (string.Equals(rol, Roles.Admin, StringComparison.OrdinalIgnoreCase))
            efectivos.Add(CatalogoPermisos.RolesAdministrar);

        return efectivos;
    }

    public void Invalidar() => _cache.Clear();
}

// ============================================================================
//  Implementaciones en MySQL
// ============================================================================

public class MySqlRoleStore(IConfiguration config) : IRoleStore
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    /// Crea la tabla `roles`, siembra los roles de sistema y alarga `usuarios.rol` a 20 caracteres.
    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        using var cn = Conexion();
        cn.Open();
        Ejecutar(cn, $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`roles` (
              `nombre`    VARCHAR(20) NOT NULL PRIMARY KEY,
              `esSistema` TINYINT(1)  NOT NULL DEFAULT 0,
              `creado`    DATETIME(6) NOT NULL
            ) ENGINE=InnoDB;
            """);
        // Los nombres de rol ya no se limitan a Admin/Ingeniero: la columna los admite tal cual.
        foreach (var r in Roles.Sistema)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT IGNORE INTO roles (nombre, esSistema, creado) VALUES (@n, 1, @c)";
            cmd.Parameters.AddWithValue("@n", r);
            cmd.Parameters.AddWithValue("@c", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<RolInfo> List()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT nombre, esSistema, creado FROM roles ORDER BY esSistema DESC, nombre";
        var lista = new List<RolInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            lista.Add(new RolInfo(r.GetString(0), r.GetBoolean(1), DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc)));
        // Garantiza que los de sistema aparezcan aunque la tabla se haya creado vacía.
        foreach (var s in Roles.Sistema)
            if (!lista.Any(x => string.Equals(x.Nombre, s, StringComparison.OrdinalIgnoreCase)))
                lista.Insert(0, new RolInfo(s, true, DateTime.UtcNow));
        return lista;
    }

    public bool Existe(string nombre)
    {
        nombre = ReglasRol.Normalizar(nombre);
        if (Roles.EsSistema(nombre)) return true;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM roles WHERE nombre = @n";
        cmd.Parameters.AddWithValue("@n", nombre);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public (int, string?) Crear(string nombre)
    {
        nombre = ReglasRol.Normalizar(nombre);
        if (ReglasRol.Validar(nombre, this) is { } e) return (400, e);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO roles (nombre, esSistema, creado) VALUES (@n, 0, @c)";
            cmd.Parameters.AddWithValue("@n", nombre);
            cmd.Parameters.AddWithValue("@c", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
            return (201, null);
        }
        catch (MySqlException ex) when (ex.Number == 1062) { return (409, "Ya existe un rol con ese nombre."); }
    }

    public (int, string?) Renombrar(string viejo, string nuevo)
    {
        viejo = ReglasRol.Normalizar(viejo);
        nuevo = ReglasRol.Normalizar(nuevo);
        if (Roles.EsSistema(viejo)) return (400, "Los roles del sistema no se pueden renombrar.");
        if (!Existe(viejo)) return (404, "El rol no existe.");
        if (nuevo.Length < ReglasRol.LargoMin || nuevo.Length > ReglasRol.LargoMax || !ReglasRol.EsNombreValido(nuevo))
            return (400, "El nombre solo puede usar letras, números, espacio, punto, guion y guion bajo (2 a 20 caracteres).");
        if (Roles.EsSistema(nuevo)) return (400, "Ese nombre está reservado por el sistema.");
        if (Existe(nuevo)) return (409, "Ya existe un rol con ese nombre.");
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var tx = cn.BeginTransaction();
            Actualizar(tx, "UPDATE roles SET nombre = @nuevo WHERE nombre = @viejo", viejo, nuevo);
            Actualizar(tx, "UPDATE permisos SET rol = @nuevo WHERE rol = @viejo", viejo, nuevo);
            Actualizar(tx, "UPDATE usuarios SET rol = @nuevo WHERE rol = @viejo", viejo, nuevo);
            tx.Commit();
            return (204, null);
        }
        catch (MySqlException ex) when (ex.Number == 1062) { return (409, "Ya existe un rol con ese nombre."); }
    }

    public (int, string?) Eliminar(string nombre)
    {
        nombre = ReglasRol.Normalizar(nombre);
        if (Roles.EsSistema(nombre)) return (400, "Los roles del sistema no se pueden eliminar.");
        using var cn = Conexion();
        cn.Open();
        using var tx = cn.BeginTransaction();
        long usuarios = Contar(tx, "SELECT COUNT(*) FROM usuarios WHERE rol = @n", nombre);
        if (usuarios > 0)
        {
            tx.Rollback();
            return (400, $"No se puede eliminar: hay {usuarios} usuario(s) con ese rol. Reasígnalos primero.");
        }
        bool existia = Contar(tx, "SELECT COUNT(*) FROM roles WHERE nombre = @n", nombre) > 0;
        if (!existia) { tx.Rollback(); return (404, "El rol no existe."); }
        EjecutarTx(tx, "DELETE FROM permisos WHERE rol = @n", nombre);
        EjecutarTx(tx, "DELETE FROM roles WHERE nombre = @n", nombre);
        tx.Commit();
        return (204, null);
    }

    // ---- helpers ----
    private static void Ejecutar(MySqlConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
    private static void Actualizar(MySqlTransaction tx, string sql, string viejo, string nuevo)
    {
        using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@viejo", viejo);
        cmd.Parameters.AddWithValue("@nuevo", nuevo);
        cmd.ExecuteNonQuery();
    }
    private static void EjecutarTx(MySqlTransaction tx, string sql, string nombre)
    {
        using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@n", nombre);
        cmd.ExecuteNonQuery();
    }
    private static long Contar(MySqlTransaction tx, string sql, string nombre)
    {
        using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@n", nombre);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}

public class MySqlUserPermissionStore(IConfiguration config) : IUserPermissionStore
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
            CREATE TABLE IF NOT EXISTS `{db}`.`usuario_permisos` (
              `usuario`   VARCHAR(40) NOT NULL,
              `clave`     VARCHAR(60) NOT NULL,
              `concedido` TINYINT(1)  NOT NULL DEFAULT 1,
              PRIMARY KEY (`usuario`, `clave`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, bool> Overrides(string usuario)
    {
        usuario = (usuario ?? "").Trim();
        var dict = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (usuario.Length == 0) return dict;
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT clave, concedido FROM usuario_permisos WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@u", usuario);
            using var r = cmd.ExecuteReader();
            while (r.Read()) dict[r.GetString(0)] = r.GetBoolean(1);
        }
        catch { /* sin tabla todavía: sin ajustes */ }
        return dict;
    }

    public (int Status, string? Error) Guardar(string usuario, IEnumerable<(string Clave, bool Concedido)> ajustes)
    {
        usuario = (usuario ?? "").Trim();
        if (usuario.Length == 0) return (400, "Falta el usuario.");
        if (UserRules.IsRoot(usuario)) return (400, "root conserva todos los permisos; no admite restricciones individuales.");
        var limpios = ajustes
            .Where(a => CatalogoPermisos.EsClaveValida(a.Clave))
            .GroupBy(a => a.Clave, StringComparer.Ordinal)
            .Select(g => (Clave: g.Key, Concedido: g.Last().Concedido))
            .ToList();
        if (limpios.Count != ajustes.Select(a => a.Clave).Distinct(StringComparer.Ordinal).Count())
            return (400, "Hay permisos desconocidos en la lista.");
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var tx = cn.BeginTransaction();
            using (var del = cn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM usuario_permisos WHERE usuario = @u";
                del.Parameters.AddWithValue("@u", usuario);
                del.ExecuteNonQuery();
            }
            foreach (var (clave, concedido) in limpios)
            {
                using var ins = cn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO usuario_permisos (usuario, clave, concedido) VALUES (@u, @c, @g)";
                ins.Parameters.AddWithValue("@u", usuario);
                ins.Parameters.AddWithValue("@c", clave);
                ins.Parameters.AddWithValue("@g", concedido);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
            return (204, null);
        }
        catch { return (500, "No se pudieron guardar los ajustes del usuario."); }
    }
}

// ============================================================================
//  Respaldo en archivo JSON (se usa si MySQL no está disponible al arrancar)
// ============================================================================

public class JsonRoleStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonRoleStore> logger) : IRoleStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:RoleFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "roles.json");

    private Dictionary<string, RolInfo> Leer()
    {
        var d = new Dictionary<string, RolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Roles.Sistema) d[s] = new RolInfo(s, true, DateTime.UtcNow);
        if (!File.Exists(_path)) return d;
        try
        {
            var guardados = JsonSerializer.Deserialize<List<RolInfo>>(File.ReadAllText(_path), Json);
            foreach (var r in guardados ?? []) d[r.Nombre] = r;
        }
        catch (Exception e) { logger.LogWarning("No se pudo leer roles.json: {Error}", e.Message); }
        return d;
    }

    private void Escribir(Dictionary<string, RolInfo> d)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(d.Values.ToList(), Json));
    }

    public IReadOnlyList<RolInfo> List() { lock (_lock) return Leer().Values.OrderByDescending(r => r.EsSistema).ThenBy(r => r.Nombre).ToList(); }
    public bool Existe(string nombre) { lock (_lock) return Leer().ContainsKey(ReglasRol.Normalizar(nombre)); }

    public (int, string?) Crear(string nombre)
    {
        nombre = ReglasRol.Normalizar(nombre);
        lock (_lock)
        {
            if (ReglasRol.Validar(nombre, this) is { } e) return (400, e);
            var d = Leer();
            d[nombre] = new RolInfo(nombre, false, DateTime.UtcNow);
            try { Escribir(d); } catch (Exception ex) { logger.LogWarning("No se pudo guardar roles.json: {Error}", ex.Message); return (500, "No se pudo crear el rol."); }
            return (201, null);
        }
    }

    public (int, string?) Renombrar(string viejo, string nuevo)
    {
        viejo = ReglasRol.Normalizar(viejo); nuevo = ReglasRol.Normalizar(nuevo);
        if (Roles.EsSistema(viejo)) return (400, "Los roles del sistema no se pueden renombrar.");
        lock (_lock)
        {
            var d = Leer();
            if (!d.ContainsKey(viejo)) return (404, "El rol no existe.");
            if (nuevo.Length < ReglasRol.LargoMin || nuevo.Length > ReglasRol.LargoMax) return (400, "Nombre de rol inválido.");
            if (Roles.EsSistema(nuevo) || d.ContainsKey(nuevo)) return (409, "Ya existe un rol con ese nombre.");
            d.Remove(viejo);
            d[nuevo] = new RolInfo(nuevo, false, DateTime.UtcNow);
            try { Escribir(d); } catch { return (500, "No se pudo renombrar el rol."); }
            return (204, null);
        }
    }

    public (int, string?) Eliminar(string nombre)
    {
        nombre = ReglasRol.Normalizar(nombre);
        if (Roles.EsSistema(nombre)) return (400, "Los roles del sistema no se pueden eliminar.");
        lock (_lock)
        {
            var d = Leer();
            if (!d.ContainsKey(nombre)) return (404, "El rol no existe.");
            d.Remove(nombre);
            try { Escribir(d); } catch { return (500, "No se pudo eliminar el rol."); }
            return (204, null);
        }
    }
}

public class JsonUserPermissionStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonUserPermissionStore> logger) : IUserPermissionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:UserPermissionFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "usuario_permisos.json");

    private Dictionary<string, Dictionary<string, bool>> Leer()
    {
        if (!File.Exists(_path)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, bool>>>(File.ReadAllText(_path), Json) ?? new(); }
        catch (Exception e) { logger.LogWarning("No se pudo leer usuario_permisos.json: {Error}", e.Message); return new(); }
    }

    public IReadOnlyDictionary<string, bool> Overrides(string usuario)
    {
        usuario = (usuario ?? "").Trim();
        lock (_lock)
            return Leer().TryGetValue(usuario, out var d) ? d : new Dictionary<string, bool>();
    }

    public (int Status, string? Error) Guardar(string usuario, IEnumerable<(string Clave, bool Concedido)> ajustes)
    {
        usuario = (usuario ?? "").Trim();
        if (usuario.Length == 0) return (400, "Falta el usuario.");
        if (UserRules.IsRoot(usuario)) return (400, "root conserva todos los permisos; no admite restricciones individuales.");
        var limpios = ajustes.Where(a => CatalogoPermisos.EsClaveValida(a.Clave))
            .GroupBy(a => a.Clave, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Concedido, StringComparer.Ordinal);
        if (limpios.Count != ajustes.Select(a => a.Clave).Distinct(StringComparer.Ordinal).Count())
            return (400, "Hay permisos desconocidos en la lista.");
        lock (_lock)
        {
            var datos = Leer();
            if (limpios.Count == 0) datos.Remove(usuario); else datos[usuario] = limpios;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(datos, Json));
            }
            catch (Exception e) { logger.LogWarning("No se pudo guardar usuario_permisos.json: {Error}", e.Message); return (500, "No se pudieron guardar los ajustes."); }
            return (204, null);
        }
    }
}
