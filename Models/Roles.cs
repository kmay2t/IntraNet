namespace Meditechnosys.Intranet.Models;

public static class Roles
{
    /// Rol protegido: siempre conserva todos los permisos y no se puede eliminar ni renombrar,
    /// para que nadie quede bloqueado fuera de la administración.
    public const string Admin = "Admin";
    /// Rol de sistema editable (no se puede eliminar ni renombrar, pero sí cambiar sus permisos).
    public const string Ingeniero = "Ingeniero";

    /// Nombre para mostrar. Los roles personalizados se muestran tal cual se crearon.
    public static string Display(string role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ? "Administrador"
        : string.Equals(role, Ingeniero, StringComparison.OrdinalIgnoreCase) ? "Ingeniero"
        : string.IsNullOrWhiteSpace(role) ? "(sin rol)"
        : role;

    /// Los dos roles que vienen con el sistema y no se pueden borrar ni renombrar.
    public static readonly IReadOnlyList<string> Sistema = new[] { Admin, Ingeniero };

    public static bool EsSistema(string role) =>
        Sistema.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}

