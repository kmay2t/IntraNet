using System.Security.Cryptography;
using System.Text.Json;
using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

// Activo con default true: los users.json anteriores a esta columna no traen el campo y
// System.Text.Json usa el valor por defecto del parametro cuando la propiedad no existe.
// Nombre y Cargo son los datos "de persona" que se imprimen en actas y credenciales y que
// se autocompletan al elegir al usuario; tambien con default para registros previos.
public record UserRecord(string Usuario, string Rol, string Hash, DateTime Creado, bool Activo = true,
    string Nombre = "", string Cargo = "");

public interface IUserStore
{
    IReadOnlyList<UserRecord> List();
    UserRecord? Find(string usuario);
    /// <returns>201 creado, 400 datos invalidos, 409 ya existe.</returns>
    (int Status, string? Error, UserRecord? User) Create(string usuario, string password, string rol, bool validatePassword = true, string nombre = "", string cargo = "");
    /// <returns>204 eliminado, 404 no existe, 400 es el ultimo administrador.</returns>
    (int Status, string? Error) Delete(string usuario);
    /// <returns>204 actualizado, 404 no existe, 400 contrasena invalida.</returns>
    (int Status, string? Error) SetPassword(string usuario, string password);
    /// <returns>204 actualizado, 404 no existe, 400 rol invalido o ultimo administrador.</returns>
    (int Status, string? Error) SetRole(string usuario, string rol);
    /// <returns>204 actualizado, 404 no existe, 400 dejaria sin administrador activo.</returns>
    (int Status, string? Error) SetActive(string usuario, bool activo);
    /// <returns>204 actualizado, 404 no existe. Guarda el nombre de persona y el cargo.</returns>
    (int Status, string? Error) SetDatos(string usuario, string nombre, string cargo);
}

public static partial class PasswordHasher
{
    private const int Iterations = 210_000;
    private static readonly string Dummy = Hash("contrasena-de-relleno");

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        // Si el usuario no existe se verifica contra un hash de relleno para que el tiempo no delate si existe.
        var parts = (stored ?? Dummy).Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var it)) return false;
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, it, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual) && stored is not null;
    }
}

// Almacen en archivo JSON (App_Data/users.json). Sirve para arrancar; para muchos usuarios o varios
// servidores conviene cambiarlo por una base de datos implementando IUserStore.
public partial class UserStore : IUserStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _lock = new();
    private readonly string _path;
    private readonly List<UserRecord> _users;

    public UserStore(IWebHostEnvironment env, IConfiguration config)
    {
        _path = config["Auth:UsersFile"] ?? Path.Combine(env.ContentRootPath, "App_Data", "users.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _users = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<UserRecord>>(File.ReadAllText(_path), Json) ?? new()
            : new();
    }

    public IReadOnlyList<UserRecord> List() { lock (_lock) return _users.OrderBy(u => u.Usuario, StringComparer.OrdinalIgnoreCase).ToList(); }

    public UserRecord? Find(string usuario)
    {
        lock (_lock) return _users.FirstOrDefault(u => u.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
    }

    public (int, string?, UserRecord?) Create(string usuario, string password, string rol, bool validatePassword = true, string nombre = "", string cargo = "")
    {
        usuario = (usuario ?? "").Trim();
        if (!UserRules.ValidName(usuario)) return (400, UserRules.NameError, null);
        var role = UserRules.NormalizeRole(rol);
        if (role is null) return (400, UserRules.RoleError, null);
        if (UserRules.IsRoot(usuario) && role != Roles.Admin) return (400, UserRules.RootProtectedError, null);
        if (validatePassword && UserRules.PasswordError(password) is { } perr) return (400, perr, null);

        lock (_lock)
        {
            if (_users.Any(u => u.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase))) return (409, "Ese usuario ya existe.", null);
            var rec = new UserRecord(usuario, role, PasswordHasher.Hash(password), DateTime.UtcNow, true,
                UserRules.LimpiarTexto(nombre, UserRules.NombreMax), UserRules.LimpiarTexto(cargo, UserRules.CargoMax));
            _users.Add(rec);
            Save();
            return (201, null, rec);
        }
    }

    public (int, string?) Delete(string usuario)
    {
        if (UserRules.IsRoot(usuario)) return (400, UserRules.RootProtectedError);
        lock (_lock)
        {
            var u = _users.FirstOrDefault(x => x.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
            if (u is null) return (404, "No existe ese usuario.");
            if (u.Rol == Roles.Admin && _users.Count(x => x.Rol == Roles.Admin) == 1) return (400, "No se puede eliminar al último administrador.");
            _users.Remove(u);
            Save();
            return (204, null);
        }
    }

    public (int, string?) SetPassword(string usuario, string password)
    {
        if (UserRules.PasswordError(password) is { } perr) return (400, perr);
        lock (_lock)
        {
            var i = _users.FindIndex(x => x.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return (404, "No existe ese usuario.");
            _users[i] = _users[i] with { Hash = PasswordHasher.Hash(password) };
            Save();
            return (204, null);
        }
    }

    public (int, string?) SetRole(string usuario, string rol)
    {
        var role = UserRules.NormalizeRole(rol);
        if (role is null) return (400, UserRules.RoleError);
        if (UserRules.IsRoot(usuario) && role != Roles.Admin) return (400, UserRules.RootProtectedError);
        lock (_lock)
        {
            var i = _users.FindIndex(x => x.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return (404, "No existe ese usuario.");
            if (_users[i].Rol == Roles.Admin && role != Roles.Admin && _users.Count(x => x.Rol == Roles.Admin) == 1)
                return (400, "No se puede quitar el rol al último administrador.");
            _users[i] = _users[i] with { Rol = role };
            Save();
            return (204, null);
        }
    }

    public (int, string?) SetActive(string usuario, bool activo)
    {
        if (UserRules.IsRoot(usuario) && !activo) return (400, UserRules.RootProtectedError);
        lock (_lock)
        {
            var i = _users.FindIndex(x => x.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return (404, "No existe ese usuario.");
            // No se puede dejar el sistema sin al menos un administrador que pueda entrar.
            if (!activo && _users[i].Rol == Roles.Admin && _users[i].Activo &&
                _users.Count(x => x.Rol == Roles.Admin && x.Activo) == 1)
                return (400, "No se puede desactivar al último administrador activo.");
            _users[i] = _users[i] with { Activo = activo };
            Save();
            return (204, null);
        }
    }

    public (int, string?) SetDatos(string usuario, string nombre, string cargo)
    {
        var n = UserRules.LimpiarTexto(nombre, UserRules.NombreMax);
        var g = UserRules.LimpiarTexto(cargo, UserRules.CargoMax);
        lock (_lock)
        {
            var i = _users.FindIndex(x => x.Usuario.Equals(usuario, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return (404, "No existe ese usuario.");
            _users[i] = _users[i] with { Nombre = n, Cargo = g };
            Save();
            return (204, null);
        }
    }

    private void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_users, Json));
        File.Move(tmp, _path, overwrite: true);
    }
}

public class StoreAuthenticator(IUserStore store) : IAuthenticator
{
    public Task<AuthUser?> ValidateAsync(string usuario, string password)
    {
        var u = store.Find(usuario ?? "");
        // Una cuenta desactivada no puede iniciar sesion aunque la contrasena sea correcta.
        if (u is null || !u.Activo) return Task.FromResult<AuthUser?>(null);
        var ok = PasswordHasher.Verify(password ?? "", u.Hash);
        return Task.FromResult(ok ? new AuthUser(u.Usuario, u.Rol) : null);
    }
}
