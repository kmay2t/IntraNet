using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Almacen de usuarios en MySQL. Misma semantica y codigos de estado que el
/// almacen JSON: la conexion se toma de ConnectionStrings:Usuarios y la base
/// y la tabla se crean solas en el primer arranque (EnsureSchema).
public class MySqlUserStore : IUserStore
{
    private readonly string _cs;

    public MySqlUserStore(IConfiguration config)
    {
        _cs = config.GetConnectionString("Usuarios")
              ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");
    }

    private MySqlConnection Conexion() => new(_cs);

    /// Crea la base y la tabla si no existen.
    public void EnsureSchema()
    {
        var b = new MySqlConnectionStringBuilder(_cs);
        var db = b.Database;
        if (string.IsNullOrEmpty(db))
            throw new InvalidOperationException("La cadena ConnectionStrings:Usuarios debe incluir Database.");
        b.Database = "";
        using var cn = new MySqlConnection(b.ConnectionString);
        cn.Open();
        Ejecutar(cn, $"CREATE DATABASE IF NOT EXISTS `{db}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");
        Ejecutar(cn, $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`usuarios` (
              `usuario` VARCHAR(40) NOT NULL PRIMARY KEY,
              `rol`     VARCHAR(20) NOT NULL,
              `hash`    VARCHAR(255) NOT NULL,
              `creado`  DATETIME(6) NOT NULL,
              `activo`  TINYINT(1) NOT NULL DEFAULT 1,
              `nombre`  VARCHAR(120) NOT NULL DEFAULT '',
              `cargo`   VARCHAR(80) NOT NULL DEFAULT ''
            ) ENGINE=InnoDB;
            """);
        // Migracion: bases creadas antes de existir la columna `activo` la reciben aqui.
        // Los registros previos quedan activos (DEFAULT 1).
        if (!ColumnaExiste(cn, db, "activo"))
            Ejecutar(cn, $"ALTER TABLE `{db}`.`usuarios` ADD COLUMN `activo` TINYINT(1) NOT NULL DEFAULT 1;");
        // Migracion: nombre de persona y cargo (datos que se imprimen en actas y credenciales y que
        // se autocompletan al elegir al usuario). Los registros previos quedan con texto vacio.
        if (!ColumnaExiste(cn, db, "nombre"))
            Ejecutar(cn, $"ALTER TABLE `{db}`.`usuarios` ADD COLUMN `nombre` VARCHAR(120) NOT NULL DEFAULT '';");
        if (!ColumnaExiste(cn, db, "cargo"))
            Ejecutar(cn, $"ALTER TABLE `{db}`.`usuarios` ADD COLUMN `cargo` VARCHAR(80) NOT NULL DEFAULT '';");
    }

    /// Dice si la tabla `usuarios` ya tiene una columna dada (para migrar sin tocar datos existentes).
    private static bool ColumnaExiste(MySqlConnection cn, string db, string columna) =>
        Escalar(cn, """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @db AND TABLE_NAME = 'usuarios' AND COLUMN_NAME = @col;
            """, ("@db", db), ("@col", columna)) > 0;

    /// Inserta un usuario ya existente (migracion desde el almacen JSON); si ya esta, no hace nada.
    public void Import(UserRecord rec)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT IGNORE INTO usuarios (usuario, rol, hash, creado, activo, nombre, cargo) VALUES (@u, @r, @h, @c, @a, @n, @g)";
        Parametros(cmd, rec);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<UserRecord> List()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT usuario, rol, hash, creado, activo, nombre, cargo FROM usuarios ORDER BY usuario";
        var lista = new List<UserRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Leer(r));
        return lista;
    }

    public UserRecord? Find(string usuario)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT usuario, rol, hash, creado, activo, nombre, cargo FROM usuarios WHERE usuario = @u";
        cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
        using var r = cmd.ExecuteReader();
        return r.Read() ? Leer(r) : null;
    }

    public (int Status, string? Error, UserRecord? User) Create(string usuario, string password, string rol, bool validatePassword = true, string nombre = "", string cargo = "")
    {
        usuario = (usuario ?? "").Trim();
        if (!UserRules.ValidName(usuario)) return (400, UserRules.NameError, null);
        var role = UserRules.NormalizeRole(rol);
        if (role is null) return (400, UserRules.RoleError, null);
        if (UserRules.IsRoot(usuario) && role != Roles.Admin) return (400, UserRules.RootProtectedError, null);
        if (validatePassword && UserRules.PasswordError(password) is { } perr) return (400, perr, null);

        using var cn = Conexion();
        cn.Open();
        using var tx = cn.BeginTransaction();
        try
        {
            if (Contar(cn, tx, "SELECT COUNT(*) FROM usuarios WHERE usuario = @u", ("@u", usuario)) > 0)
            {
                tx.Rollback();
                return (409, "Ese usuario ya existe.", null);
            }
            var rec = new UserRecord(usuario, role, PasswordHasher.Hash(password), DateTime.UtcNow, true,
                UserRules.LimpiarTexto(nombre, UserRules.NombreMax), UserRules.LimpiarTexto(cargo, UserRules.CargoMax));
            using (var cmd = cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO usuarios (usuario, rol, hash, creado, activo, nombre, cargo) VALUES (@u, @r, @h, @c, @a, @n, @g)";
                Parametros(cmd, rec);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return (201, null, rec);
        }
        catch (MySqlException e) when (e.Number == 1062)
        {
            tx.Rollback();
            return (409, "Ese usuario ya existe.", null);
        }
    }

    public (int Status, string? Error) Delete(string usuario)
    {
        if (UserRules.IsRoot(usuario)) return (400, UserRules.RootProtectedError);
        using var cn = Conexion();
        cn.Open();
        using var tx = cn.BeginTransaction();
        string? rol;
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT rol FROM usuarios WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
            rol = cmd.ExecuteScalar() as string;
        }
        if (rol is null) { tx.Rollback(); return (404, "No existe ese usuario."); }
        if (rol == Roles.Admin && Contar(cn, tx, "SELECT COUNT(*) FROM usuarios WHERE rol = @r", ("@r", Roles.Admin)) == 1)
        {
            tx.Rollback();
            return (400, "No se puede eliminar al último administrador.");
        }
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM usuarios WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return (204, null);
    }

    public (int Status, string? Error) SetPassword(string usuario, string password)
    {
        if (UserRules.PasswordError(password) is { } perr) return (400, perr);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE usuarios SET hash = @h WHERE usuario = @u";
        cmd.Parameters.AddWithValue("@h", PasswordHasher.Hash(password));
        cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
        return cmd.ExecuteNonQuery() == 1 ? (204, null) : (404, "No existe ese usuario.");
    }

    public (int Status, string? Error) SetRole(string usuario, string rol)
    {
        var role = UserRules.NormalizeRole(rol);
        if (role is null) return (400, UserRules.RoleError);
        if (UserRules.IsRoot(usuario) && role != Roles.Admin) return (400, UserRules.RootProtectedError);
        using var cn = Conexion();
        cn.Open();
        using var tx = cn.BeginTransaction();
        string? actual;
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT rol FROM usuarios WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
            actual = cmd.ExecuteScalar() as string;
        }
        if (actual is null) { tx.Rollback(); return (404, "No existe ese usuario."); }
        if (actual == Roles.Admin && role != Roles.Admin &&
            Contar(cn, tx, "SELECT COUNT(*) FROM usuarios WHERE rol = @r", ("@r", Roles.Admin)) == 1)
        {
            tx.Rollback();
            return (400, "No se puede quitar el rol al último administrador.");
        }
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE usuarios SET rol = @r WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@r", role);
            cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return (204, null);
    }

    public (int Status, string? Error) SetActive(string usuario, bool activo)
    {
        if (UserRules.IsRoot(usuario) && !activo) return (400, UserRules.RootProtectedError);
        using var cn = Conexion();
        cn.Open();
        using var tx = cn.BeginTransaction();
        string? rol = null; bool actual = false; bool existe = false;
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT rol, activo FROM usuarios WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
            using var r = cmd.ExecuteReader();
            if (r.Read()) { existe = true; rol = r.GetString(0); actual = r.GetBoolean(1); }
        }
        if (!existe) { tx.Rollback(); return (404, "No existe ese usuario."); }
        // No se puede dejar el sistema sin al menos un administrador que pueda entrar.
        if (!activo && actual && rol == Roles.Admin &&
            Contar(cn, tx, "SELECT COUNT(*) FROM usuarios WHERE rol = @r AND activo = 1", ("@r", Roles.Admin)) == 1)
        {
            tx.Rollback();
            return (400, "No se puede desactivar al último administrador activo.");
        }
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE usuarios SET activo = @a WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@a", activo);
            cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return (204, null);
    }

    public (int Status, string? Error) SetDatos(string usuario, string nombre, string cargo)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE usuarios SET nombre = @n, cargo = @g WHERE usuario = @u";
        cmd.Parameters.AddWithValue("@n", UserRules.LimpiarTexto(nombre, UserRules.NombreMax));
        cmd.Parameters.AddWithValue("@g", UserRules.LimpiarTexto(cargo, UserRules.CargoMax));
        cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
        return cmd.ExecuteNonQuery() == 1 ? (204, null) : (404, "No existe ese usuario.");
    }

    // ---- helpers ----

    private static void Ejecutar(MySqlConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Contar(MySqlConnection cn, MySqlTransaction tx, string sql, params (string Nombre, string Valor)[] ps)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (nombre, valor) in ps) cmd.Parameters.AddWithValue(nombre, valor);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void Parametros(MySqlCommand cmd, UserRecord rec)
    {
        cmd.Parameters.AddWithValue("@u", rec.Usuario);
        cmd.Parameters.AddWithValue("@r", rec.Rol);
        cmd.Parameters.AddWithValue("@h", rec.Hash);
        cmd.Parameters.AddWithValue("@c", rec.Creado);
        cmd.Parameters.AddWithValue("@a", rec.Activo);
        cmd.Parameters.AddWithValue("@n", rec.Nombre);
        cmd.Parameters.AddWithValue("@g", rec.Cargo);
    }

    private static UserRecord Leer(MySqlDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetString(2),
            DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc), r.GetBoolean(4),
            r.GetString(5), r.GetString(6));

    /// Ejecuta una consulta escalar (sin transaccion) y devuelve el resultado como long.
    private static long Escalar(MySqlConnection cn, string sql, params (string Nombre, string Valor)[] ps)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nombre, valor) in ps) cmd.Parameters.AddWithValue(nombre, valor);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
