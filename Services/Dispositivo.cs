using System.Text.RegularExpressions;

namespace Meditechnosys.Intranet.Services;

/// <summary>
/// Convierte la cadena <c>User-Agent</c> que manda el navegador en una descripción corta y legible
/// del dispositivo con el que se entró a la intranet:
/// <list type="bullet">
/// <item><c>PC · Windows 10/11 · Chrome 131</c></item>
/// <item><c>Teléfono · Android 13 · SM-A515F · Chrome 130</c></item>
/// <item><c>Tableta · iPadOS 17.2 · Safari</c></item>
/// <item><c>Aplicación · curl</c> cuando no es un navegador</item>
/// </list>
/// Es solo texto: se deduce del encabezado que el propio navegador manda con cada petición, no se
/// consulta a ningún servicio externo ni se guarda el User-Agent completo. Si el cliente no lo manda,
/// la descripción queda vacía. Lo usa la bitácora de accesos en cada renglón.
/// </summary>
public static class Dispositivo
{
    /// <summary>Longitud máxima que se guarda (la columna <c>dispositivo</c> de MySQL mide lo mismo).</summary>
    public const int Max = 120;

    private static readonly Regex VersionAndroid = new(@"Android\s+([\d]+(?:\.[\d]+)?)", RegexOptions.Compiled);
    private static readonly Regex ModeloAndroid = new(@"Android\s+[\d.]+(?:\s*;\s*([^;)]+?))?\)", RegexOptions.Compiled);
    private static readonly Regex VersionIos = new(@"(?:iPhone OS|CPU OS|iOS)\s+([\d_]+)", RegexOptions.Compiled);
    private static readonly Regex VersionMac = new(@"Mac OS X\s*([\d._]+)", RegexOptions.Compiled);
    private static readonly Regex RxWindows = new(@"Windows NT\s+([\d.]+)", RegexOptions.Compiled);
    private static readonly Regex VersionCros = new(@"CrOS\s+\S+\s+([\d.]+)", RegexOptions.Compiled);

    /// <summary>Descripción legible del dispositivo, o cadena vacía si no se puede deducir nada.</summary>
    public static string Describir(string? userAgent)
    {
        var ua = (userAgent ?? "").Trim();
        if (ua.Length == 0) return "";

        var partes = new List<string>();
        var tipo = "";

        // ---------- herramientas y clientes que no son navegador ----------
        // La app de escritorio, el quemador, curl o una prueba con Postman también abren sesiones;
        // conviene distinguirlas de una persona sentada frente a un equipo.
        var app = Aplicacion(ua);
        if (app is not null)
        {
            return Recorta(string.Join(" · ", new[] { "Aplicación", app }));
        }

        // ---------- sistema operativo y forma del equipo ----------
        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
        {
            tipo = "Teléfono";
            partes.Add("iPhone" + (VersionIos.Match(ua) is { Success: true } v ? " iOS " + v.Groups[1].Value.Replace('_', '.') : ""));
        }
        else if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            tipo = "Tableta";
            partes.Add("iPad" + (VersionIos.Match(ua) is { Success: true } v ? " iPadOS " + v.Groups[1].Value.Replace('_', '.') : ""));
        }
        else if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            // Los tablet Android suelen decir "Mobile" igual que los teléfonos; si trae "Tablet"
            // o el modelo empieza con SM-T (Samsung) se toma como tableta.
            var esTableta = ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ||
                            (ModeloAndroid.Match(ua) is { Success: true } m && m.Groups[1].Value.Trim().StartsWith("SM-T",
                                StringComparison.OrdinalIgnoreCase));
            tipo = esTableta ? "Tableta" : "Teléfono";
            var so = "Android" + (VersionAndroid.Match(ua) is { Success: true } v ? " " + v.Groups[1].Value : "");
            var modelo = ModeloAndroid.Match(ua) is { Success: true } mm ? LimpiaModelo(mm.Groups[1].Value) : "";
            // "Linux; Android 13" sin modelo (algunos emuladores y WebView) deja solo el sistema.
            partes.Add(modelo.Length > 0 ? $"{so} · {modelo}" : so);
        }
        else if (ua.Contains("Windows NT", StringComparison.OrdinalIgnoreCase))
        {
            tipo = ua.Contains("Phone", StringComparison.OrdinalIgnoreCase) ? "Teléfono" : "PC";
            var win = VersionWindows(ua);
            partes.Add(win.Length > 0 ? "Windows " + win : "Windows");
        }
        else if (ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) || ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase))
        {
            // Safari en macOS ya no se distingue de iPad por "Mobile"; el token "Macintosh" manda.
            tipo = "Mac";
            partes.Add("macOS" + (VersionMac.Match(ua) is { Success: true } v ? " " + v.Groups[1].Value.Trim('_', '.') : ""));
        }
        else if (ua.Contains("CrOS", StringComparison.OrdinalIgnoreCase))
        {
            tipo = "Chromebook";
            partes.Add("ChromeOS" + (VersionCros.Match(ua) is { Success: true } v ? " " + v.Groups[1].Value : ""));
        }
        else if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) || ua.Contains("X11", StringComparison.OrdinalIgnoreCase))
        {
            tipo = "PC";
            partes.Add("Linux");
        }

        // ---------- navegador ----------
        var navegador = Navegador(ua);
        if (navegador.Length > 0) partes.Add(navegador);

        if (tipo.Length == 0 && partes.Count == 0) return "";
        var texto = tipo.Length > 0 ? string.Join(" · ", new[] { tipo }.Concat(partes)) : string.Join(" · ", partes);
        return Recorta(texto);
    }

    /// <summary>Cliente que no es un navegador humano, o null si sí lo es.</summary>
    private static string? Aplicacion(string ua)
    {
        // El orden importa: "Mozilla/5.0 ... Chrome/" también lo traen muchas herramientas.
        if (ua.StartsWith("curl/", StringComparison.OrdinalIgnoreCase)) return Nombre(ua, "curl");
        if (ua.StartsWith("Wget/", StringComparison.OrdinalIgnoreCase)) return Nombre(ua, "wget");
        if (ua.StartsWith("python-requests", StringComparison.OrdinalIgnoreCase)) return "Python requests";
        if (ua.StartsWith("python-urllib", StringComparison.OrdinalIgnoreCase)) return "Python urllib";
        if (ua.StartsWith("node-fetch", StringComparison.OrdinalIgnoreCase)) return "Node fetch";
        if (ua.StartsWith("axios/", StringComparison.OrdinalIgnoreCase)) return "Axios";
        if (ua.StartsWith("Go-http-client", StringComparison.OrdinalIgnoreCase)) return "Go";
        if (ua.StartsWith("Java/", StringComparison.OrdinalIgnoreCase)) return "Java";
        if (ua.Contains("WinHTTP", StringComparison.OrdinalIgnoreCase)) return "WinHTTP";
        if (ua.Contains("PowerShell", StringComparison.OrdinalIgnoreCase)) return "PowerShell";
        if (ua.Contains("PostmanRuntime", StringComparison.OrdinalIgnoreCase)) return "Postman";
        if (ua.Contains("fo-dicom", StringComparison.OrdinalIgnoreCase)) return "fo-dicom";
        // El quemador DICOM y las apps propias se identifican con su nombre de agente.
        if (ua.Contains("Meditechnosys", StringComparison.OrdinalIgnoreCase) ||
            ua.Contains("Quemador", StringComparison.OrdinalIgnoreCase)) return "App Meditechnosys";
        return null;
    }

    private static string Nombre(string ua, string base_) => base_ + Version(ua, base_ + "/");

    /// <summary>Navegador con su versión mayor: "Chrome 131", "Edge 130", "Safari".</summary>
    private static string Navegador(string ua)
    {
        // Edge y Opera se revisan antes que Chrome: sus User-Agent incluyen el token "Chrome".
        foreach (var (clave, nombre) in new[]
                 {
                     ("EdgA/", "Edge"), ("Edg/", "Edge"), ("Edge/", "Edge"),
                     ("OPR/", "Opera"), ("Opera/", "Opera"),
                     ("SamsungBrowser/", "Samsung Internet"),
                     ("MiuiBrowser/", "Mi Browser"),
                     ("Firefox/", "Firefox"), ("FxiOS/", "Firefox"),
                     ("CriOS/", "Chrome"), ("Chrome/", "Chrome"), ("Chromium/", "Chromium"),
                     ("UCBrowser/", "UC Browser"),
                     ("Version/", null), // Safari: la versión real viene después de "Version/"
                 })
        {
            var i = ua.IndexOf(clave, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            if (nombre is null)
            {
                // Solo es Safari si no es Chrome/Edge disfrazado (esos ya se contestaron arriba).
                return ua.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari" + Version(ua, "Version/") : "";
            }
            return nombre + Version(ua, clave);
        }
        return ua.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari" : "";
    }

    /// <summary>Número de versión mayor que sigue a <paramref name="clave"/>, o cadena vacía.</summary>
    private static string Version(string ua, string clave)
    {
        var i = ua.IndexOf(clave, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        var desde = i + clave.Length;
        var fin = desde;
        while (fin < ua.Length && (char.IsDigit(ua[fin]) || ua[fin] == '.')) fin++;
        var v = ua[desde..fin].Trim('.');
        if (v.Length == 0) return "";
        var punto = v.IndexOf('.');
        return " " + (punto > 0 ? v[..punto] : v);   // solo la versión mayor: 131, no 131.0.6778.86
    }

    /// <summary>Nombre comercial de la versión de Windows que anuncia el User-Agent.</summary>
    private static string VersionWindows(string ua)
    {
        var v = RxWindows.Match(ua) is { Success: true } m ? m.Groups[1].Value : "";
        return v switch
        {
            "10.0" => "10/11",   // Windows 11 se sigue anunciando como Windows NT 10.0
            "6.3" => "8.1",
            "6.2" => "8",
            "6.1" => "7",
            "6.0" => "Vista",
            "5.1" => "XP",
            _ => ""
        };
    }

    /// <summary>Quita sufijos y espacios del modelo Android ("SM-A515F Build/..." → "SM-A515F").</summary>
    private static string LimpiaModelo(string modelo)
    {
        var m = modelo.Trim();
        var corte = m.IndexOfAny(new[] { ' ', ',', ';' });
        if (corte > 0) m = m[..corte];
        // "Build/TP1A.220624.014" y "wv" (WebView) no aportan nada, y Chrome manda "K"
        // como marcador de modelo cuando no quiere revelar el teléfono.
        if (m.Length < 3) return "";
        return m.Equals("Build", StringComparison.OrdinalIgnoreCase) || m.EndsWith("wv", StringComparison.OrdinalIgnoreCase)
            ? "" : m;
    }

    private static string Recorta(string s) => s.Length <= Max ? s : s[..Max].TrimEnd(' ', '·', ' ');
}
