using System.Security.Claims;
using Meditechnosys.Intranet.Models;
using Meditechnosys.Intranet.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Meditechnosys.Intranet.Pages;

/// Datos del panel lateral *Avisos* de la página de inicio. `Todos` incluye también los avisos
/// pausados: quien administra los ve (marcados como pausados) para poder reactivarlos, editarlos o
/// eliminarlos; quien no administra ve únicamente los activos.
public record AvisosPanelModel(IReadOnlyList<Aviso> Todos, bool PuedeAdministrar);

/// Página de inicio única. Muestra solamente las herramientas a las que el usuario tiene acceso
/// según sus permisos efectivos (matriz del rol + ajustes por usuario), que viajan en la sesión
/// como claims `perm`. Sirve igual para el Administrador, el Ingeniero y cualquier rol personalizado.
/// El panel *Avisos* lo ven todos los usuarios con sesión; editarlo exige `avisos.administrar`.
public class IndexModel : PageModel
{
    private readonly IAvisoStore _avisos;
    private readonly ServiciosOptions _servicios;

    public IndexModel(IAvisoStore avisos, IOptions<ServiciosOptions> servicios)
    {
        _avisos = avisos;
        _servicios = servicios.Value;
    }

    /// Herramientas visibles para este usuario (filtradas por permiso en el servidor).
    public IReadOnlyList<AppLink> Apps { get; private set; } = new List<AppLink>();
    public IReadOnlyList<SystemStatus> Systems => Catalog.Status();
    public string Usuario => User.Identity?.Name ?? "";
    public string Rol => Roles.Display(User.FindFirst(ClaimTypes.Role)?.Value ?? "");

    /// Panel de avisos (lista + bandera de si este usuario puede publicar/editar/eliminar).
    public AvisosPanelModel Panel { get; private set; } = new(new List<Aviso>(), false);

    public void OnGet()
    {
        var permisos = User.FindAll(GestionService.PermClaim).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        Apps = Catalog.Visibles(permisos, _servicios);

        IReadOnlyList<Aviso> todos;
        try { todos = _avisos.List(); }
        catch { todos = new List<Aviso>(); } // si el almacén no responde, el inicio sigue mostrando herramientas
        Panel = new AvisosPanelModel(todos, permisos.Contains(CatalogoPermisos.AvisosAdministrar));
    }
}
