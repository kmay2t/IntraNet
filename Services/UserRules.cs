using System.Text.RegularExpressions;
using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

/// Reglas de validacion compartidas por todos los almacenes de usuarios.
public static partial class UserRules
{
    public const string NameError = "El usuario debe tener de 3 a 40 caracteres: letras, números, punto, guion o guion bajo.";
    public const string RoleError = "El rol no es válido.";
    public const string RootProtectedError = "root es el superusuario: no se puede eliminar, desactivar ni cambiar su rol de administrador.";

    public static bool IsRoot(string? usuario) => string.Equals(usuario?.Trim(), "root", StringComparison.OrdinalIgnoreCase);

    public const int NombreMax = 120;
    public const int CargoMax = 80;

    /// Recorta y limpia un texto "de persona" (nombre o cargo); null/vacio queda como "".
    public static string LimpiarTexto(string? valor, int max) => (valor ?? "").Trim()[..Math.Min((valor ?? "").Trim().Length, max)];

    public static bool ValidName(string usuario) => ValidNameRegex().IsMatch(usuario);

    /// Mínimo de caracteres de la contraseña de la intranet. Lo pidió el área operativa: se acepta
    /// desde 3. El hash es PBKDF2-SHA256 con 210 000 iteraciones, así que la regla corta no cambia
    /// cómo se guarda; solo deja pasar contraseñas breves.
    public const int PasswordMin = 3;

    public static string? PasswordError(string? p) =>
        string.IsNullOrEmpty(p) || p.Length < PasswordMin
            ? "La contraseña debe tener al menos " + PasswordMin + " caracteres."
            : null;

    /// Devuelve el nombre canónico del rol: los roles de sistema se normalizan a su forma exacta y
    /// cualquier otro rol personalizado se acepta tal cual si tiene un nombre válido. `null` si el
    /// rol viene vacío o con caracteres no permitidos. La **existencia** del rol se comprueba aparte
    /// contra `IRoleStore` en el endpoint (aquí solo se valida la forma del nombre).
    public static string? NormalizeRole(string? rol)
    {
        var r = (rol ?? "").Trim();
        if (r.Length == 0) return null;
        if (string.Equals(r, Roles.Admin, StringComparison.OrdinalIgnoreCase)) return Roles.Admin;
        if (string.Equals(r, Roles.Ingeniero, StringComparison.OrdinalIgnoreCase)) return Roles.Ingeniero;
        return ReglasRol.EsNombreValido(r) ? r : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,40}$")]
    private static partial Regex ValidNameRegex();
}
