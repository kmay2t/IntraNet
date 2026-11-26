namespace Meditechnosys.Intranet.Services;

/// Tema de cada cuenta —**Oscuro** o **Claro**—, el que se elige en el bloque «Tema» del botón ⚙.
///
/// Antes era una preferencia de **este equipo**: vivía en `localStorage` y la movía el interruptor
/// Día/Noche de la cabecera. Ahora vive en **la cuenta**, en la misma tabla de configuración y con el
/// mismo reparto por usuario que ya usa el tiempo de sesión (`sesion.minutos.<usuario>`), así que la
/// persona entra a cualquier equipo del hospital y se encuentra su pantalla como la dejó.
///
/// Como el servidor no puede esperar a una llamada de API para pintar —las pantallas autónomas son
/// archivos estáticos y el `<script>` del `<head>` corre antes del primer pintado—, el valor de la
/// cuenta viaja al navegador en una cookie **no HttpOnly** (`meditechnosys.tema`). La cookie no manda
/// sobre la cuenta: es una copia. La escribe el acceso, la refresca `PUT /api/tema`, la mantiene al día
/// el middleware de `Program.cs` en cada petición autenticada y la borra la salida; `js/theme.js` solo
/// la lee. Si falta o viene sucia, se pinta **Oscuro**, que es el valor por defecto de este cambio.
public static class TemaPreferencias
{
    /// Valores que entiende la interfaz. Son los mismos que escribe `data-theme` en `<html>`.
    public const string Oscuro = "dark";
    public const string Claro = "light";

    /// Con este cambio la intranet abre en **Oscuro** para quien no haya elegido nada todavía.
    public const string PorDefecto = Oscuro;

    /// Cookie que lleva el tema al navegador para aplicarlo antes del primer pintado.
    public const string CookieName = "meditechnosys.tema";

    /// Prefijo de la clave por usuario en el almacén de configuración.
    public const string PrefijoClave = "tema.";

    /// Vida de la cookie: un año. No caduca nunca por sí sola porque el servidor la reescribe en
    /// cuanto la cuenta cambia de tema o se vuelve a entrar.
    public static TimeSpan VidaCookie { get; } = TimeSpan.FromDays(365);

    /// Las dos opciones que ofrece la API y el diálogo ⚙. Nada fuera de esta lista se acepta.
    public static readonly IReadOnlyList<TemaOpcion> Opciones = new[]
    {
        new TemaOpcion(Oscuro, "Oscuro"),
        new TemaOpcion(Claro, "Claro"),
    };

    /// Clave donde vive el tema de ese usuario, normalizada a minúsculas porque los nombres de cuenta
    /// se guardan sin distinción de mayúsculas.
    public static string Clave(string usuario)
        => PrefijoClave + (usuario ?? "").Trim().ToLowerInvariant();

    /// `"dark"`, `"light"` o `null`. Acepta los nombres de la pantalla («oscuro», «claro») y `null`
    /// significa «no es un tema válido».
    public static string? Normalizar(string? valor) => (valor ?? "").Trim().ToLowerInvariant() switch
    {
        Oscuro or "oscuro" or "noche" => Oscuro,
        Claro or "claro" or "dia" or "día" => Claro,
        "" or null => null,
        _ => null,
    };

    public static bool EsOpcion(string? valor) => Normalizar(valor) is not null;

    /// El tema guardado de ese usuario. Un valor viejo, vacío o fuera de la lista se lee como el
    /// predeterminado (Oscuro), para que una preferencia escrita a mano no deje a nadie sin pantalla.
    public static string Valor(IConfigStore config, string? usuario)
    {
        if (string.IsNullOrWhiteSpace(usuario)) return PorDefecto;
        return Normalizar(config.Get(Clave(usuario!), PorDefecto)) ?? PorDefecto;
    }

    /// Cómo se llama ese valor en pantalla («Oscuro» / «Claro»).
    public static string Etiqueta(string tema)
        => Opciones.FirstOrDefault(o => o.Valor == tema)?.Etiqueta ?? tema;

    /// Atajo para quien ya tiene el `HttpContext` en la mano: la cookie se marca `Secure` solo cuando
    /// la petición entró por https (producción), no en el `http://localhost` del desarrollo.
    public static void EscribirEnEn(HttpContext ctx, string tema)
        => EscribirEn(ctx.Response.Cookies, tema, ctx.Request.IsHttps);

    /// Escribe la cookie. `HttpOnly=false` a propósito: `js/theme.js` necesita leerla antes del
    /// pintado. No lleva nada sensible —solo «dark» o «light»— y viaja con `SameSite=Lax`, como la de
    /// sesión. El candado `Secure` lo pone la llamada: en producción (https) va marcado y en el
    /// desarrollo de `http://localhost` no, que es exactamente lo que haría `SameAsRequest`.
    public static void EscribirEn(IResponseCookies cookies, string tema, bool esHttps)
        => cookies.Append(CookieName, Normalizar(tema) ?? PorDefecto, new CookieOptions
        {
            Path = "/",
            HttpOnly = false,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = esHttps,
            MaxAge = VidaCookie,
        });

    /// Se deshace de ella al cerrar la sesión, para que el siguiente usuario que entre en este equipo
    /// no herede el tema del anterior.
    public static void Borrar(IResponseCookies cookies)
        => cookies.Delete(CookieName, new CookieOptions { Path = "/", SameSite = SameSiteMode.Lax });

    /// Lo que trajo el navegador, ya normalizado; `null` si no hay cookie o si trae basura.
    public static string? LeerDe(IRequestCookieCollection cookies)
        => Normalizar(cookies[CookieName]);
}

/// Un tema que ofrece la API (`valor` interno + cómo se le llama en pantalla).
public record TemaOpcion(string Valor, string Etiqueta);
