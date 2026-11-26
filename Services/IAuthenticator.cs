namespace Meditechnosys.Intranet.Services;

public record AuthUser(string Name, string Role);

public interface IAuthenticator
{
    /// <summary>Devuelve el usuario con su rol si las credenciales son validas; null si no.</summary>
    Task<AuthUser?> ValidateAsync(string usuario, string password);
}

// Usuarios iniciales (Auth:Users). Solo se usan para crear el almacen la primera vez, cuando esta vacio.
// No hay cuentas ni contraseñas predeterminadas. El primer administrador puede crearse con
// Auth__Users__0__User=root / Auth__Users__0__Password / Auth__Users__0__Role=Admin.
public class SeedUserEntry
{
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "";
}
