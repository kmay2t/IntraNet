using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meditechnosys.Intranet.Services;

/// <summary>
/// Opciones de la sección <c>Https</c> de appsettings.json.
/// </summary>
public sealed class OpcionesHttps
{
    /// <summary>Si la intranet debe abrirse también en https. Verdadero por omisión.</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Puerto seguro. El HTTP de siempre (5023) sigue funcionando junto a este.</summary>
    public int Puerto { get; set; } = 5024;

    /// <summary>
    /// Nombres o IPs que deben caber en el certificado y que la intranet no puede adivinar: la
    /// dirección con la que entran los usuarios (p. ej. <c>10.252.252.200</c>) y cualquier alias del
    /// hospital (<c>intranet.hospital</c>). Se escriben aquí porque son los puntos que, de faltar,
    /// obligan a armar un certificado nuevo; las demás tarjetas de la máquina se incluyen al crearlo
    /// pero no lo renuevan cuando cambian solas por DHCP.
    /// </summary>
    public string[] Nombres { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Cuando es verdadero, quien entra por http:// recibe el salto solo a la dirección segura.
    /// Se deja apagado hasta que el certificado esté confiado en las PC del hospital, para no dejar
    /// a nadie fuera.
    /// </summary>
    public bool Redirigir { get; set; }
}

/// <summary>
/// El certificado con el que la intranet se abre en https://, creado por ella misma la primera vez
/// y guardado en <c>App_Data/https</c> para que sea siempre el mismo.
///
/// Por qué existe: el navegador solo quiere entregar la posición real de un aparato (GPS o wifi del
/// puesto) cuando la página viene en una dirección segura. En http:// el permiso ni siquiera se
/// puede pedir, así que la bitácora se queda con la aproximación que da la salida a Internet de ese
/// equipo, que es zona y no edificio. Este certificado propio abre esa puerta sin contratar nada.
///
/// Qué trae dentro: el nombre de esta máquina, «localhost» y las direcciones IPv4 que tiene puestas,
/// además de los alias escritos en <see cref="OpcionesHttps.Nombres"/>.
///
/// Cuándo se arma uno nuevo: solo cuando falta alguno de esos puntos <b>fijos</b> (el nombre de la
/// máquina, «localhost» y los alias de la configuración) o cuando vence. Las direcciones IPv4 que da
/// el DHCP —el wifi u otra tarjeta secundaria— entran en el certificado al crearlo pero no son
/// motivo para regenerarlo, porque el arriendo cambia solo y cada cambio dejaba un certificado nuevo
/// que rompía al Quemador (que solo acepta la hojita <c>intranet.cer</c> que traía instalada) y obligaba
/// a volver a confiar en todas las PC. La dirección con la que entran a la intranet —la fija del
/// hospital— va escrita en <c>Https:Nombres</c>; así, si esa sí cambia, el certificado se renueva
/// como debe.
///
/// Un reinicio del servidor no lo toca: al volver a arrancar el servicio se vuelve a cargar el mismo
/// archivo, con la misma huella, y las PC siguen entrando sin volver a confiar nada. Para que tampoco
/// lo pierda un borrón del <c>App_Data</c> (una republicación, otra carpeta, un formateo mal puesto),
/// cada arranque deja un espejo del trío en <c>C:\ProgramData\Meditechnosys\Intranet\https</c>, fuera
/// de la carpeta del proyecto: si al arrancar no hay certificado en App_Data pero sí lo hay en el
/// espejo, se trae el del espejo y el certificado sigue siendo el mismo de siempre; solo se arma uno
/// nuevo cuando tampoco existe el espejo. Cada arranque deja su renglón —«certificado … reutilizado /
/// restaurado del respaldo / creado (huella …)»— en <c>arranque.txt</c>, junto al espejo (y en el bitácora
/// que le pase quien lo llama), que es donde se comprueba que un reinicio no cambió nada.
///
/// No es de una autoridad pública: al abrirlo por primera vez el navegador advierte que no lo conoce.
/// La advertencia se quita instalando <c>intranet.cer</c> en «Entidades de certificación raíz de
/// confianza» de cada PC, que es lo que hace <c>confiar-certificado.bat</c>. Y no bloquea nada: con
/// certificado propio —aunque todavía no esté confiado— el navegador ya tiene la página por segura y
/// deja pedir el permiso de ubicación.
/// </summary>
public static class CertificadoIntranet
{
    /// <summary>El certificado cargado en memoria, la carpeta donde vive y su huella.</summary>
    public sealed record Resultado(X509Certificate2 Certificado, string Carpeta, string RutaCer, string Huella);

    /// <summary>Años de vida: se crea una vez y no hay que renovarlo cada seis meses.</summary>
    private const int AnosVida = 10;

    private static string PfxEn(string carpeta) => Path.Combine(carpeta, "intranet.pfx");
    private static string CerEn(string carpeta) => Path.Combine(carpeta, "intranet.cer");
    private static string ClaveEn(string carpeta) => Path.Combine(carpeta, "intranet.clave");

    /// <summary>
    /// Espejo del certificado fuera de la carpeta del proyecto: <c>C:\ProgramData\Meditechnosys\Intranet\https</c>.
    /// Vive ahí porque ahí lo dejan intacto una republicación, el borrado de <c>App_Data</c> o que el
    /// proyecto cambie de ruta o de unidad —y mientras exista, el certificado que las PC ya confían
    /// puede volver a cargarse tal cual. Con <c>INTRANET_RESPALDO_CERTIFICADO</c> se pone otro
    /// destino (una carpeta de red donde también copie, o una prueba en una carpeta prestada).
    /// </summary>
    private static string CarpetaRespaldo()
    {
        var dicho = Environment.GetEnvironmentVariable("INTRANET_RESPALDO_CERTIFICADO");
        if (!string.IsNullOrWhiteSpace(dicho)) return dicho.Trim();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Meditechnosys", "Intranet", "https");
    }

    /// <summary>
    /// Devuelve el certificado listo para el servidor, creándolo si no existe o si ya no cubre los
    /// puntos fijos de hoy. <paramref name="avisa"/> recibe en texto lo que pasó (crear, renovar,
    /// fallar) para que el arranque lo deje en el bitácora. Además cada renglón se copia a
    /// <c>arranque.txt</c> junto al espejo, porque el visor de eventos de Windows solo guarda los
    /// mensajes del servicio (iniciar/detener), no los Information de la aplicación: así queda dónde
    /// leer la huella con la que arrancó hoy y comparar con la de ayer. Si algo no se puede escribir
    /// o firmar devuelve null y la intranet sigue sirviendo por http://: ninguna pantalla depende de esto.
    /// </summary>
    public static Resultado? Asegura(string carpeta, IEnumerable<string>? alias, Action<string>? avisa = null)
    {
        void nota(string m)
        {
            try { avisa?.Invoke(m); } catch { /* el bitácora nunca frena un arranque */ }
            EscribirArranque(m);
        }
        try
        {
            Directory.CreateDirectory(carpeta);

            var nombres = (alias ?? Array.Empty<string>()).ToList();
            // Lo que tiene que caber en un certificado nuevo: los fijos más las IPs de hoy.
            var deseados = DireccionesDeseadas(nombres);
            // Lo que hace falta que cubra el ya guardado: solo los fijos (las IPs del DHCP cambian
            // solas y no deben traer abajo el certificado en el que las PC ya confían).
            var fijos = DireccionesFijas(nombres);

            var resultado = Intenta(carpeta, fijos, nota);
            var como = "reutilizado";
            if (resultado is null)
            {
                // No se pudo leer el de App_Data. Antes de armar uno nuevo —que dejaría fuera al
                // Quemador y a todas las PC que ya confían en la hojita vieja— se busca el espejo.
                if (TraerDelRespaldo(carpeta, nota))
                {
                    resultado = Intenta(carpeta, fijos, nota);
                    if (resultado is not null) como = "restaurado del respaldo";
                }
                if (resultado is null)
                {
                    Crea(carpeta, deseados, nota);
                    resultado = Intenta(carpeta, fijos, nota);
                    como = "creado";
                }
            }
            if (resultado is not null)
            {
                // El espejo se mantiene al día para el próximo arranque; si no se puede escribir,
                // nada de esto cambia el servicio: solo queda sin respaldo.
                Respaldar(carpeta, nota);
                nota($"certificado https {como} (huella {resultado.Huella}, vence {resultado.Certificado.NotAfter:d/M/yyyy}); la dirección segura sigue siendo la misma.");
            }
            return resultado;
        }
        catch (Exception e)
        {
            nota($"no se pudo preparar el certificado https ({e.Message}); la intranet sigue solo en http.");
            return null;
        }
    }

    /// <summary>Nombre propio + localhost + las IPv4 de la máquina + los alias de la configuración.</summary>
    private static SortedSet<string> DireccionesDeseadas(IEnumerable<string> alias)
    {
        var salida = DireccionesFijas(alias);
        foreach (var ip in IpsLocales()) salida.Add(ip);
        return salida;
    }

    /// <summary>
    /// Los puntos que no dependen de la red del momento: «localhost», la dirección de retorno, el
    /// nombre de esta máquina y los alias escritos en <c>Https:Nombres</c> (ahí va la IP fija con la
    /// que se entra a la intranet). Estos son los únicos que, de faltar en el certificado guardado,
    /// justifica armar uno nuevo.
    /// </summary>
    private static SortedSet<string> DireccionesFijas(IEnumerable<string> alias)
    {
        var salida = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1" };
        var maquina = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(maquina))
        {
            salida.Add(maquina);
            salida.Add(maquina.ToLowerInvariant());
        }
        foreach (var a in alias)
        {
            var s = (a ?? "").Trim();
            if (s.Length > 0) salida.Add(s);
        }
        return salida;
    }

    /// <summary>Las IPv4 asignadas a esta máquina, sin las de enlace local automático (169.254.x).</summary>
    public static IEnumerable<string> IpsLocales()
    {
        var lista = new List<string>();
        try
        {
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                var s = ip.ToString();
                if (s.StartsWith("169.254.")) continue;
                lista.Add(s);
            }
        }
        catch { /* la red no responde: se queda con localhost y el nombre de la máquina */ }
        return lista;
    }

    /// <summary>
    /// Copia el espejo de <see cref="CarpetaRespaldo"/> de vuelta a <paramref name="carpeta"/>. Devuelve
    /// false si no hay espejo completo o si no se pudo escribir: en ese caso quien llama decide si arma
    /// uno nuevo. Nunca borra lo que hubiera: lo que está ahí ya se descartó por ilegible o incompleto.
    /// </summary>
    private static bool TraerDelRespaldo(string carpeta, Action<string> nota)
    {
        var respaldo = CarpetaRespaldo();
        var hayTodos = File.Exists(PfxEn(respaldo)) && File.Exists(ClaveEn(respaldo)) && File.Exists(CerEn(respaldo));
        if (!hayTodos) return false;
        try
        {
            Copiar(PfxEn(respaldo), PfxEn(carpeta));
            Copiar(ClaveEn(respaldo), ClaveEn(carpeta));
            Copiar(CerEn(respaldo), CerEn(carpeta));
            nota("el certificado https no estaba (o no se podía leer) en App_Data; se trajo el del respaldo de ProgramData.");
            return true;
        }
        catch (Exception e)
        {
            nota($"el respaldo del certificado https no se pudo usar ({e.Message}).");
            return false;
        }
    }

    /// <summary>
    /// Deja el trío (pfx, clave y hojita .cer) copiado en <see cref="CarpetaRespaldo"/> para que un
    /// reinicio, una republicación o un App_Data borrado no signifique un certificado nuevo. Solo
    /// reescribe los archivos que de verdad cambiaron, así el arranque de todos los días no toca disco.
    /// Un fallo aquí no rompe nada: se avisa y la intranet sirve igual.
    /// </summary>
    private static void Respaldar(string carpeta, Action<string> nota)
    {
        try
        {
            var respaldo = CarpetaRespaldo();
            Directory.CreateDirectory(respaldo);
            foreach (var nombre in new[] { "intranet.pfx", "intranet.clave", "intranet.cer" })
            {
                var origen = Path.Combine(carpeta, nombre);
                if (!File.Exists(origen)) continue;
                var destino = Path.Combine(respaldo, nombre);
                if (Iguales(origen, destino)) continue;
                Copiar(origen, destino);
            }
        }
        catch (Exception e)
        {
            nota($"el respaldo del certificado https no se pudo actualizar ({e.Message}); el certificado sigue sirviendo igual.");
        }
    }

    /// <summary>Escribe primero en un archivo suelto y después lo cambia de nombre: no deja a medio copiar.</summary>
    private static void Copiar(string desde, string hacia)
    {
        var temporal = hacia + ".nuevo";
        File.Copy(desde, temporal, true);
        File.Move(temporal, hacia, true);
    }

    /// <summary>Si dos archivos pesan y firman igual, para no reescribir el respaldo cada arranque.</summary>
    private static bool Iguales(string a, string b)
    {
        if (!File.Exists(b)) return false;
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var sha = SHA256.Create();
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        return sha.ComputeHash(fa).SequenceEqual(sha.ComputeHash(fb));
    }

    /// <summary>
    /// Deja una línea por aviso del certificado en <c>arranque.txt</c>, junto al espejo: es el
    /// comprobante que queda después de un reinicio (o de diez) para comparar huellas y ver que nunca
    /// se rehizo el certificado. Fallar aquí no cambia nada: es un cuaderno, no una dependencia.
    /// </summary>
    private static void EscribirArranque(string mensaje)
    {
        try
        {
            var carpeta = CarpetaRespaldo();
            Directory.CreateDirectory(carpeta);
            File.AppendAllText(Path.Combine(carpeta, "arranque.txt"),
                $"{DateTime.Now:dd/MM/yyyy HH:mm:ss}  {mensaje}{Environment.NewLine}");
        }
        catch { /* el comprobante es opcional; servir por https no depende de él */ }
    }

    /// <summary>
    /// Carga el par pfx/clave si existe, está vigente y cubre los puntos fijos. Las IPs que ya no son
    /// las de hoy se quedan dentro sin molestar: sacarlas no aporta nada y rehacer el certificado por
    /// ese motivo rompe la confianza de las PC que ya tienen la hojita instalada.
    /// </summary>
    private static Resultado? Intenta(string carpeta, SortedSet<string> fijos, Action<string> nota)
    {
        var pfx = PfxEn(carpeta);
        var clave = ClaveEn(carpeta);
        if (!File.Exists(pfx) || !File.Exists(clave)) return null;
        try
        {
            var cert = new X509Certificate2(pfx, File.ReadAllText(clave).Trim());
            if (cert.NotAfter <= DateTime.UtcNow.AddDays(30))
            {
                nota("el certificado https vence en menos de un mes; toca rehacerlo.");
                cert.Dispose();
                return null;
            }
            var cubre = NombresDel(cert);
            // Si no se logra leer qué trae, se da por bueno: regenerarlo cada arranque obligaría a
            // volver a confiar el certificado en todas las PC.
            foreach (var d in cubre.Count == 0 ? Array.Empty<string>() : fijos.Where(x => !cubre.Contains(x)))
            {
                nota($"el certificado https no cubre {d}; toca rehacerlo.");
                cert.Dispose();
                return null;
            }
            return new Resultado(cert, carpeta, CerEn(carpeta), cert.Thumbprint);
        }
        catch (Exception e)
        {
            nota($"el certificado https guardado no se pudo leer ({e.Message}); se busca en el respaldo.");
            return null;
        }
    }

    /// <summary>
    /// Los nombres e IPs que ya trae un certificado, para compararlos con los de hoy. .NET 8 no trae
    /// EnumerateDomainNames y el texto que arma la extensión viene traducido al idioma de Windows
    /// («Nombre DNS=localhost», «Dirección IP=10.252.252.200» en español), así que se parte cada
    /// renglón y se toma lo que viene después de la etiqueta: la etiqueta cambia de idioma, el valor no.
    /// </summary>
    private static SortedSet<string> NombresDel(X509Certificate2 cert)
    {
        var salida = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in cert.Extensions)
        {
            if (ext is not X509SubjectAlternativeNameExtension san) continue;
            foreach (var parte in san.Format(false).Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = parte.Trim();
                var i = s.IndexOf('=');
                if (i < 0) i = s.IndexOf(':');
                var valor = i >= 0 && i + 1 < s.Length ? s[(i + 1)..].Trim() : "";
                if (valor.Length > 0) salida.Add(valor);
            }
        }
        return salida;
    }

    /// <summary>
    /// Firma y guarda el certificado propio: el par (pfx + clave) para el servidor y la hojita .cer
    /// (solo llave pública) para que las PC de usuarios lo den por bueno.
    /// </summary>
    private static void Crea(string carpeta, SortedSet<string> deseados, Action<string> nota)
    {
        using var rsa = RSA.Create(2048);
        var peticion = new CertificateRequest(
            $"CN=Intranet Meditechnosys ({Environment.MachineName})", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        foreach (var d in deseados)
        {
            // Lo que se puede leer como dirección IP entra como IP (así lo revisa el navegador cuando
            // alguien escribe la IP en la barra); lo demás entra como nombre de equipo.
            if (IPAddress.TryParse(d, out var ip)) san.AddIpAddress(ip);
            else san.AddDnsName(d);
        }
        peticion.CertificateExtensions.Add(san.Build());
        peticion.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        peticion.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        peticion.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },   // autenticación de servidor
            false));

        var antes = DateTime.UtcNow.AddDays(-1);          // colchón por PCs con el reloj corrido
        var despues = antes.AddYears(AnosVida);
        using var nuevo = peticion.CreateSelfSigned(antes, despues);

        // La contraseña del pfx es aleatoria y se guarda junto a él dentro de App_Data, carpeta que
        // la intranet nunca sirve por HTTP. No va en appsettings.json para que no la lea cualquiera
        // que abra el archivo de configuración.
        var clave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        File.WriteAllBytes(PfxEn(carpeta), nuevo.Export(X509ContentType.Pkcs12, clave));
        File.WriteAllText(ClaveEn(carpeta), clave);
        File.WriteAllBytes(CerEn(carpeta), nuevo.Export(X509ContentType.Cert));

        nota($"certificado https propio creado (vence el {despues:d/M/yyyy}): {string.Join(", ", deseados)}.");
    }
}
