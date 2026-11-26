using Meditechnosys.Intranet.Models;
using Meditechnosys.Intranet.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Meditechnosys.Intranet.Pages;

public class LogoutModel(IAuditLog bitacora) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        var usuario = User.Identity?.Name;
        if (!string.IsNullOrEmpty(usuario))
            bitacora.Register(usuario, AuditEvents.Logout, "Web", HttpContext.Connection.RemoteIpAddress?.ToString());

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // La copia del tema se va con la sesión: si no, la siguiente persona que entre en este equipo
        // vería la pantalla como la dejó la anterior.
        TemaPreferencias.Borrar(HttpContext.Response.Cookies);
        return RedirectToPage("/Login");
    }
}
