using Meditechnosys.Intranet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Meditechnosys.Intranet.Pages;

[Authorize(Policy = "P:" + CatalogoPermisos.DicomVer)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ListaTrabajoModel : PageModel
{
    public void OnGet() { }
}
