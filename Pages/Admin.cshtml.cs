using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Meditechnosys.Intranet.Pages;

/// Ruta legacy: la página de Administración se unificó en la página de inicio ("/"), que muestra
/// solo las herramientas a las que el usuario tiene acceso. Se conserva para no romper enlaces y
/// favoritos, redirigiendo al inicio.
public class AdminModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");
}
