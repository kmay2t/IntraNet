using System.Security.Claims;
using Meditechnosys.Intranet.Models;
using Meditechnosys.Intranet.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Meditechnosys.Intranet.Pages;

public class LoginModel(IAuthenticator authenticator, IAuditLog bitacora, IConfigStore config) : PageModel
{
    [BindProperty] public string Usuario { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public bool Remember { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    /// `?sesion=expirada`: llega puesto por la propia autenticación cuando el tiempo de sesión que
    /// la persona eligió en el botón ⚙ del inicio se cumplió sin actividad. Sirve para decirlo en
    /// pantalla en lugar de dejarla pensando que su contraseña dejó de servir.
    [BindProperty(SupportsGet = true)] public string? Sesion { get; set; }
    public string? Error { get; set; }
    public bool SesionExpiro => string.Equals(Sesion, "expirada", StringComparison.OrdinalIgnoreCase);

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturn());
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var usuario = (Usuario ?? "").Trim();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var user = usuario.Length == 0 || string.IsNullOrEmpty(Password) ? null : await authenticator.ValidateAsync(usuario, Password);
        if (user is null)
        {
            bitacora.Register(usuario, AuditEvents.LoginFallido, "Web", ip);
            Error = "Usuario o contraseña incorrectos.";
            Password = "";
            return Page();
        }

        bitacora.Register(user.Name, AuditEvents.Login, "Web", ip);
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role) },
            CookieAuthenticationDefaults.AuthenticationScheme);

        // La caducidad absoluta de la cookie se escribe un poco más larga que la ventana más larga que
        // se puede elegir (8 h + 10 min, `SesionPreferencias.VidaCookie`). No es un descuido: si caducara
        // en las mismas 8 horas justas, el framework mataría el ticket antes de mirar la ventana de esta
        // persona y la regresaría a esta pantalla **en silencio**, sin el aviso de arriba. Quien corta la
        // sesión es `SesionPreferencias.Expiro`, que sí conoce el tiempo que cada quien eligió.
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = Remember,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(SesionPreferencias.VidaCookie),
            });

        // El tema también es de la cuenta, pero la pantalla se pinta antes de cualquier llamada: se le
        // deja al navegador la copia en la cookie para que el primer fotograma ya salga con él. Sin esta
        // línea, la persona que eligió Claro vería Oscuro hasta la siguiente navegación.
        TemaPreferencias.EscribirEnEn(HttpContext, TemaPreferencias.Valor(config, user.Name));

        return LocalRedirect(SafeReturn());
    }

    private string SafeReturn() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
