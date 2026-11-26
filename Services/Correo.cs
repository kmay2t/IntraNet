using System.Text;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using Meditechnosys.Intranet.Models;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Meditechnosys.Intranet.Services;

/// Acceso a los buzones por IMAP/SMTP (MailKit): lista cabeceras, abre el contenido de un mensaje,
/// descarga adjuntos e imagenes internas y envia mensajes (responder, responder a todos, reenviar,
/// redactar) con CC, CCO, adjuntos e imagenes incrustadas.
///
/// Credenciales: cada usuario puede tener su buzón enlazado (<see cref="IBuzonStore"/>); si no lo
/// tiene, se usa el buzón de soporte de appsettings.json (Correo:Imap). La contrasena se guarda
/// cifrada y solo se descifra en memoria para conectar: nunca se registra, nunca se devuelve al
/// navegador y los errores que se muestran al usuario van SIN datos sensibles.
/// Nunca lanza: un fallo (servidor caido, credenciales invalidas, sin red) se convierte en un
/// mensaje legible.
public sealed class CorreoService
{
    /// Tamano maximo de un archivo adjunto o de una imagen insertada en el cuerpo.
    public const int AdjuntoMaxBytes = 12 * 1024 * 1024;

    private readonly CorreoImapOptions _global;
    private readonly IBuzonStore _buzones;
    private readonly BuzonCrypto _crypto;

    public CorreoService(IOptions<CorreoImapOptions> opciones, IBuzonStore buzones, BuzonCrypto crypto)
    {
        _global = opciones.Value;
        _buzones = buzones;
        _crypto = crypto;
    }

    /// Opciones del buzón de soporte (appsettings). Es la cuenta que **todos** tienen siempre.
    public CorreoImapOptions Predeterminado => BuzonOpciones.Copiar(_global);

    /// true si el buzón de soporte tiene host, usuario y contrasena configurados.
    public bool GlobalConfigurado =>
        !string.IsNullOrWhiteSpace(_global.Host) &&
        !string.IsNullOrWhiteSpace(_global.Usuario) &&
        !string.IsNullOrWhiteSpace(_global.Contrasena);

    // ---- las dos cuentas de la bandeja ----
    // `soporte` es el buzón de appsettings.json y `propio` el segundo correo que el administrador
    // enlaza en Usuarios. Antes se excluían: si el usuario traía buzón enlazado, la bandeja dejaba
    // de ver el de soporte. Ahora son dos cuentas simultáneas, la pantalla elige con cuál lee y con
    // cuál envía, y el soporte viene marcado por defecto.
    public const string CuentaSoporte = "soporte";
    public const string CuentaPropia = "propio";

    /// Cualquier valor que no diga exactamente «propio» se toma como soporte: es la cuenta que nunca
    /// falta y la que se pide al abrir la bandeja o al redactar.
    public static string NormalizaCuenta(string? cuenta) =>
        string.Equals(cuenta?.Trim(), CuentaPropia, StringComparison.OrdinalIgnoreCase) ? CuentaPropia : CuentaSoporte;

    /// El buzon de la cuenta pedida. Devuelve null y un error legible cuando esa cuenta no se puede
    /// usar todavía (no hay segundo correo enlazado, la contrasena no descifra o el soporte no está
    /// configurado). Nunca menciona la contrasena.
    public (CorreoImapOptions? Opciones, string? Error) Resolver(string usuario, string? cuenta)
    {
        if (NormalizaCuenta(cuenta) == CuentaPropia)
        {
            var b = _buzones.Obtener(usuario);
            if (b is null)
                return (null, "Todavía no hay segundo correo enlazado para este usuario: lo conecta el "
                            + "administrador en Usuarios, en el bloque «Buzón de correo».");
            if (FalloDelEnlace(b) is { } error) return (null, error);
            return (BuzonOpciones.Desde(_global, b, _crypto.Descifrar(b.PasswordCifrada)!), null);
        }
        if (GlobalConfigurado) return (Predeterminado, null);
        return (null, "El buzon de soporte aun no esta configurado: falta la contrasena en appsettings.json (seccion Correo:Imap).");
    }

    /// Sin cuenta en la petición se usa soporte, que es la cuenta que siempre existe.
    public (CorreoImapOptions? Opciones, string? Error) Resolver(string usuario) => Resolver(usuario, CuentaSoporte);

    /// Las dos cuentas de la persona, en el orden en que las muestra la bandeja: soporte primero.
    /// Las dos viajan sin contrasenas; `Aviso` explica por qué una de ellas no se puede abrir aún y
    /// `Enlazado` dice si realmente hay un buzón guardado (la bandeja solo pinta la pestaña del
    /// segundo correo cuando lo hay).
    public IReadOnlyList<CuentaBuzon> Cuentas(string? usuario)
    {
        var u = (usuario ?? "").Trim();
        var soporte = new CuentaBuzon(CuentaSoporte, _global.Usuario, _global.RemitenteNombre, true, GlobalConfigurado,
            GlobalConfigurado ? null
                : "El buzón de soporte no está configurado: falta la contraseña en appsettings.json (sección Correo:Imap).",
            true);
        var b = u.Length > 0 ? _buzones.Obtener(u) : null;
        var aviso = b is null ? "Sin segundo correo enlazado: lo conecta el administrador en Usuarios." : FalloDelEnlace(b);
        // `Enlazado` dice si de verdad hay un buzón guardado para esta persona, aunque no funcione:
        // la bandeja usa ese dato para decidir si muestra o no la pestaña del segundo correo
        // (sin enlace hay una sola pestaña; con enlace roto la pestaña sale punteada con el motivo).
        var propio = new CuentaBuzon(CuentaPropia, b?.Correo ?? "", b?.RemitenteNombre ?? "", false,
            b is not null && aviso is null, aviso, b is not null);
        return new[] { soporte, propio };
    }

    /// Nombre de la cuenta activa, para la bitácora: la dirección real con la que salió el mensaje.
    public string CorreoDeCuenta(string usuario, string? cuenta) =>
        NormalizaCuenta(cuenta) == CuentaPropia
            ? (_buzones.Obtener(usuario)?.Correo ?? CuentaPropia)
            : _global.Usuario;

    /// Porque un enlace enlazado no sirve, dicho con exactitud y sin tocar la contrasena:
    /// null cuando esta completo y legible. Se muestra tal cual al usuario y al administrador.
    public string? FalloDelEnlace(BuzonGuardado b)
    {
        if (string.IsNullOrWhiteSpace(b.Correo) || string.IsNullOrWhiteSpace(b.ImapHost))
            return "El buzon enlazado esta incompleto: falta el correo o el servidor de entrada. "
                 + "Pide al administrador que lo vuelva a guardar en Usuarios.";
        if (string.IsNullOrWhiteSpace(b.PasswordCifrada))
            return "El buzon enlazado (" + b.Correo + ") no tiene contrasena guardada: "
                 + "pide al administrador que la escriba en Usuarios.";
        if (_crypto.Descifrar(b.PasswordCifrada) is { } claro && claro.Length > 0) return null;
        if (BuzonCrypto.EsEmpaqueAntiguo(b.PasswordCifrada))
            return "El buzon (" + b.Correo + ") tiene la contrasena guardada en un formato de una version anterior "
                 + "que ya no se puede leer. El administrador debe volver a escribirla en Usuarios y guardar; "
                 + "el resto del enlace sigue sirviendo.";
        return BuzonCrypto.PareceCifrado(b.PasswordCifrada)
            ? "La contrasena guardada del buzon (" + b.Correo + ") no se puede leer con la clave actual del servidor. "
              + "El administrador debe volver a escribirla en Usuarios y guardar: el resto del enlace sigue sirviendo."
            : "La contrasena guardada del buzon (" + b.Correo + ") no es valida: el administrador debe volver a "
              + "escribirla en Usuarios y guardar.";
    }

    /// Mismo aviso pero por nombre de usuario (lo usan la bandeja y la pantalla de Usuarios).
    public string? FalloDelEnlace(string usuario)
    {
        var b = _buzones.Obtener(usuario);
        return b is null ? null : FalloDelEnlace(b);
    }

    /// Vista antigua de una sola cuenta: si el usuario trae buzón enlazado, ese; si no, soporte.
    /// La bandeja ya no decide con esto (usa <see cref="Cuentas"/> y la cuenta que se pida en cada
    /// petición); se conserva porque la pantalla de Usuarios y `/api/correos/mio` lo siguen leyendo.
    public BuzonEnlazado EstadoEnlace(string usuario)
    {
        var b = _buzones.Obtener(usuario);
        if (b is not null) return new BuzonEnlazado(b.Correo, true, true);
        return new BuzonEnlazado(_global.Usuario, false, false);
    }

    public IReadOnlyList<BuzonPublico> TodosEnlazados() =>
        _buzones.Todos().Select(b => BuzonOpciones.Publico(b, _crypto)).ToList();

    public BuzonPublico? Publico(string usuario) => BuzonOpciones.Publico(_buzones.Obtener(usuario), _crypto);

    /// Guarda (o limpia) el buzon de un usuario. La contrasena se cifra aqui mismo; si viene vacia
    /// y no es edicion con "mantener", se pide. Nunca se guarda algo que despues no se pueda leer:
    /// si la clave del servidor no esta en disco, o si la que se quiere conservar ya no descifra,
    /// se devuelve el aviso para que el administrador la reescriba.
    public (int Status, string? Error) GuardarBuzon(string usuario, BuzonRequest r)
    {
        if (r.Limpiar) return _buzones.Guardar(usuario, r, null, null);
        if (BuzonRules.Error(r) is { } err) return (400, err);
        if (!_crypto.ClavePermanente)
            return (500, "El servidor no pudo abrir su archivo de claves de buzones (App_Data/buzon-clave.bin): "
                       + "sin el no se puede guardar ninguna contrasena. Revisa los permisos de esa carpeta y "
                       + "reinicia el servicio MeditechnosysIntranet.");
        var previo = _buzones.Obtener(usuario);
        string? plano = null;
        if (!string.IsNullOrWhiteSpace(r.Password)) plano = _crypto.Cifrar(r.Password.Trim());
        else if (previo is not null && r.MantenerContrasena)
        {
            if (_crypto.Descifrar(previo.PasswordCifrada) is not { Length: > 0 })
                return (400, "La contrasena que estaba guardada ya no se puede leer: vuelvela a escribir "
                            + "y guarda de nuevo el buzon.");
            plano = previo.PasswordCifrada;
        }
        if (string.IsNullOrEmpty(plano))
            return (400, "Falta la contrasena del buzon: escribela para poder guardar el enlace.");
        return _buzones.Guardar(usuario, r, plano, previo);
    }

    public void Desvincular(string usuario) => _buzones.Eliminar(usuario);

    /// Prueba las credenciales SIN guardarlas: conecta por IMAP (lectura) y por SMTP (salida).
    /// Devuelve null si ambas responden o un mensaje legible con el primer fallo.
    public async Task<string?> ProbarAsync(CorreoImapOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.Usuario))
            return "Falta el servidor o el correo del buzon.";
        if (string.IsNullOrEmpty(o.Contrasena)) return "Falta la contrasena del buzon.";
        var errorLectura = await ProbarLecturaAsync(o).ConfigureAwait(false);
        if (errorLectura is not null) return errorLectura;
        return await ProbarSalidaAsync(o).ConfigureAwait(false);
    }

    private async Task<string?> ProbarLecturaAsync(CorreoImapOptions o)
    {
        try
        {
            var (cliente, _) = await ConectarLecturaAsync(o).ConfigureAwait(false);
            await CierreAsync(cliente).ConfigureAwait(false);
            return null;
        }
        catch (Exception e)
        {
            return "No se pudo conectar a la entrada (IMAP " + o.Host + ":" + o.Port + "): " + e.Message;
        }
    }

    private async Task<string?> ProbarSalidaAsync(CorreoImapOptions o)
    {
        var host = string.IsNullOrWhiteSpace(o.SmtpHost) ? o.Host : o.SmtpHost;
        try
        {
            using var smtp = new SmtpClient { Timeout = TimeoutMs(o) };
            await smtp.ConnectAsync(host, o.SmtpPort, SslSmtp(o)).ConfigureAwait(false);
            await smtp.AuthenticateAsync(o.Usuario, o.Contrasena).ConfigureAwait(false);
            try { await smtp.DisconnectAsync(true).ConfigureAwait(false); } catch { /* cierre cordial */ }
            return null;
        }
        catch (Exception e)
        {
            return "No se pudo conectar a la salida (SMTP " + host + ":" + o.SmtpPort + "): " + e.Message;
        }
    }

    private static int TimeoutMs(CorreoImapOptions o) =>
        (int)TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSegundos, 5, 60)).TotalMilliseconds;

    private static SecureSocketOptions SslImap(CorreoImapOptions o) =>
        o.Ssl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

    private static SecureSocketOptions SslSmtp(CorreoImapOptions o) =>
        o.SmtpSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

    /// Conecta y autentica contra IMAP. El llamador es dueno del cliente (debe cerrarlo).
    private static async Task<ImapClient> ConectarAsync(CorreoImapOptions o)
    {
        var cliente = new ImapClient { Timeout = TimeoutMs(o) };
        await cliente.ConnectAsync(o.Host, o.Port, SslImap(o)).ConfigureAwait(false);
        await cliente.AuthenticateAsync(o.Usuario, o.Contrasena).ConfigureAwait(false);
        return cliente;
    }

    /// Se lanza cuando la carpeta pedida (Enviados / Borradores) no existe en este buzón, para que
    /// la pantalla pueda explicarlo en vez de mostrar una bandeja vacia sin motivo.
    private sealed class CarpetaNoEncontrada : Exception
    {
        public CarpetaNoEncontrada(string? logica) : base(logica ?? "") { }
    }

    private static bool EsEntrada(string? logica) =>
        string.IsNullOrEmpty(logica) ||
        logica.Equals("recibidos", StringComparison.OrdinalIgnoreCase) ||
        logica.Equals("inbox", StringComparison.OrdinalIgnoreCase);

    private static string MensajeFaltaCarpeta(string? logica) =>
        logica != null && logica.Equals("enviados", StringComparison.OrdinalIgnoreCase)
            ? "Esta cuenta no tiene carpeta de Enviados."
            : "Esta cuenta no tiene carpeta de Borradores.";

    /// Resuelve la carpeta fisica que corresponde a una opcion del menú: "recibidos" (bandeja),
    /// "enviados" o "borradores". Busca por los nombres habituales en espanol y en inglés, primero
    /// entre las subcarpetas del buzón (incluidas las anidadas bajo INBOX) y luego por nombre directo.
    /// Devuelve null si la carpeta no existe.
    private static async Task<IMailFolder?> ResolverCarpetaAsync(ImapClient c, string? logica)
    {
        if (EsEntrada(logica)) return c.Inbox;
        var enviados = logica!.Equals("enviados", StringComparison.OrdinalIgnoreCase);

        var hojas = enviados
            ? new[] { "Sent", "Sent Items", "Sent Messages", "Enviados", "Enviado", "OUTBOX" }
            : new[] { "Drafts", "Draft", "Draft Messages", "Borradores", "Borrador" };

        var todas = new List<IMailFolder>();
        try
        {
            if (c.PersonalNamespaces.Count > 0)
            {
                var raiz = c.GetFolder(c.PersonalNamespaces[0]);
                foreach (var f in await raiz.GetSubfoldersAsync(true).ConfigureAwait(false))
                    if (f is { Exists: true }) todas.Add(f);
            }
        }
        catch { /* algunos servidores no dejan listar todo: se intenta por nombre directo */ }

        foreach (var h in hojas)
        {
            var hit = todas.FirstOrDefault(f => string.Equals(f.Name, h, StringComparison.OrdinalIgnoreCase))
                      ?? todas.FirstOrDefault(f => f.Name.EndsWith(h, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        foreach (var h in hojas)
            foreach (var prefijo in new[] { "INBOX.", "INBOX/", "" })
                try
                {
                    var f = await c.GetFolderAsync(prefijo + h).ConfigureAwait(false);
                    if (f is { Exists: true }) return f;
                }
                catch { /* no existe con ese prefijo */ }
        return null;
    }

    /// Crea la carpeta de Enviados/Borradores bajo la bandeja si el servidor lo permite (algunos
    /// buzones no traen esas carpetas y aun asi conviene poder guardar). Null si no se pudo crear.
    private static async Task<IMailFolder?> CrearCarpetaAsync(ImapClient c, string? logica)
    {
        var nombre = logica != null && logica.Equals("enviados", StringComparison.OrdinalIgnoreCase) ? "Sent" : "Drafts";
        try
        {
            var f = await c.Inbox.CreateAsync(nombre, true).ConfigureAwait(false);
            return f is { Exists: true } ? f : null;
        }
        catch { return null; }
    }

    /// Conecta, resuelve la carpeta pedida y la abre. Para "recibidos" respeta la opcion Correo:Imap:Carpeta.
    /// Si la carpeta Enviados/Borradores no existe lanza <see cref="CarpetaNoEncontrada"/>. El llamador
    /// es dueno del cliente devuelto (debe cerrarlo).
    private static async Task<(ImapClient cliente, IMailFolder carpeta)> ConectarLecturaAsync(
        CorreoImapOptions o, string? logica = null, FolderAccess acceso = FolderAccess.ReadOnly)
    {
        var cliente = await ConectarAsync(o).ConfigureAwait(false);
        IMailFolder? carpeta;
        if (EsEntrada(logica))
        {
            carpeta = cliente.Inbox;
            if (!string.IsNullOrWhiteSpace(o.Carpeta) &&
                !string.Equals(o.Carpeta, "INBOX", StringComparison.OrdinalIgnoreCase))
            {
                var otra = await cliente.GetFolderAsync(o.Carpeta).ConfigureAwait(false);
                if (otra is not null) carpeta = otra;
            }
        }
        else
        {
            carpeta = await ResolverCarpetaAsync(cliente, logica).ConfigureAwait(false);
            if (carpeta is null)
            {
                await CierreAsync(cliente).ConfigureAwait(false);
                throw new CarpetaNoEncontrada(logica);
            }
        }
        await carpeta.OpenAsync(acceso).ConfigureAwait(false);
        return (cliente, carpeta);
    }

    private static async Task CierreAsync(ImapClient cliente)
    {
        try { await cliente.DisconnectAsync(true).ConfigureAwait(false); } catch { /* cierre cordial, no critico */ }
    }

    /// Anexa un mensaje ya construido a una carpeta (Enviados o Borradores). Si la carpeta no existe
    /// intenta crearla. Devuelve null si se guardo o un mensaje legible sin datos sensibles: la copia
    /// a Enviados es un extra que nunca debe fallar el envio, y guardar un borrador si avisa cuando
    /// no se pudo guardar.
    /// <paramref name="marcas"/> lleva la marca \Draft en los borradores, para que los demas clientes
    /// de correo (Outlook, Thunderbird) reconozcan el mensaje como un borrador a medias.
    private static async Task<string?> AnexarAsync(CorreoImapOptions o, string? logica, MimeMessage mensaje,
        MessageFlags marcas = MessageFlags.None)
    {
        ImapClient? cliente = null;
        try
        {
            cliente = await ConectarAsync(o).ConfigureAwait(false);
            var folder = await ResolverCarpetaAsync(cliente, logica).ConfigureAwait(false)
                         ?? await CrearCarpetaAsync(cliente, logica).ConfigureAwait(false);
            if (folder is null) return MensajeFaltaCarpeta(logica);

            // La carpeta se abre con permiso de escritura: APPEND la cierra al terminar y, sin este
            // paso, marcar el borrador (\Draft) fallaria con la carpeta ya cerrada.
            await folder.OpenAsync(FolderAccess.ReadWrite).ConfigureAwait(false);
            var anexado = await folder.AppendAsync(mensaje).ConfigureAwait(false);
            // La marca \Draft se pone aparte: anexar y luego marcar es la forma compatible en MailKit 4.x.
            if (marcas != MessageFlags.None && anexado.HasValue)
                await folder.AddFlagsAsync(anexado.Value, marcas, true).ConfigureAwait(false);
            await CierreAsync(cliente).ConfigureAwait(false);
            cliente = null;
            return null;
        }
        catch (CarpetaNoEncontrada e) { return MensajeFaltaCarpeta(e.Message); }
        catch (Exception e) { return "No se pudo guardar en el buzon: " + e.Message; }
        finally { if (cliente is not null) await CierreAsync(cliente).ConfigureAwait(false); }
    }

    /// Cabeceras de los mensajes mas recientes de una carpeta (recibidos, enviados o borradores),
    /// para la tabla de la bandeja.
    public async Task<ResultadoCorreo> RecientesAsync(CorreoImapOptions o, int? max = null, string? carpeta = null)
    {
        if (!EstaConfigurado(o))
            return new ResultadoCorreo(false, Array.Empty<MensajeCorreo>(),
                "El buzon no esta configurado: pide al administrador que te enlace un correo en Usuarios.");

        var tope = Math.Clamp(max ?? o.Maximo, 1, 200);
        try
        {
            var (cliente, folder) = await ConectarLecturaAsync(o, carpeta, FolderAccess.ReadOnly).ConfigureAwait(false);
            var esEntrada = EsEntrada(carpeta);
            var total = folder.Count;
            var lista = new List<MensajeCorreo>();
            if (total > 0)
            {
                var inicio = Math.Max(0, total - tope);
                var resumen = await folder
                    .FetchAsync(inicio, total - 1, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId)
                    .ConfigureAwait(false);
                foreach (var m in resumen)
                {
                    var env = m.Envelope;
                    // En la bandeja se muestra quien envia; en Enviados/Borradores lo util es ver a
                    // quien se mando, asi que ahi se usa el primer destinatario (y el remitente de reserva).
                    string? de = esEntrada
                        ? env?.From?.Mailboxes.FirstOrDefault()?.Address
                        : env?.To?.Mailboxes.FirstOrDefault()?.Address ?? env?.From?.Mailboxes.FirstOrDefault()?.Address;
                    if (string.IsNullOrWhiteSpace(de))
                    {
                        var bruto = esEntrada ? env?.From?.ToString() : env?.To?.ToString();
                        de = string.IsNullOrWhiteSpace(bruto) ? "(sin direccion)" : bruto!;
                    }
                    var asunto = string.IsNullOrWhiteSpace(env?.Subject) ? "(sin asunto)" : env!.Subject!;
                    var fecha = env?.Date ?? DateTimeOffset.Now;
                    lista.Add(new MensajeCorreo(m.UniqueId.Id, de!, asunto, fecha));
                }
            }
            await CierreAsync(cliente).ConfigureAwait(false);
            lista = lista.OrderByDescending(x => x.Fecha).ToList();
            return new ResultadoCorreo(true, lista, null);
        }
        catch (CarpetaNoEncontrada e)
        {
            return new ResultadoCorreo(true, Array.Empty<MensajeCorreo>(), MensajeFaltaCarpeta(e.Message));
        }
        catch (Exception e)
        {
            // e.Message describe el fallo de red/autenticacion; nunca contiene la contrasena.
            return new ResultadoCorreo(true, Array.Empty<MensajeCorreo>(),
                "No se pudo leer el buzon: " + e.Message);
        }
    }

    public static bool EstaConfigurado(CorreoImapOptions o) =>
        !string.IsNullOrWhiteSpace(o.Host) &&
        !string.IsNullOrWhiteSpace(o.Usuario) &&
        !string.IsNullOrWhiteSpace(o.Contrasena);

    /// Contenido completo de un mensaje (cabeceras, cuerpo en texto y HTML, adjuntos e imagenes
    /// internas). Las imagenes del cuerpo se indexan aparte para reescribir los `cid:` de la pantalla.
    public async Task<MensajeDetalle?> DetalleAsync(CorreoImapOptions o, uint uid, string? carpeta = null)
    {
        if (!EstaConfigurado(o)) return null;
        try
        {
            var (cliente, folder) = await ConectarLecturaAsync(o, carpeta, FolderAccess.ReadOnly).ConfigureAwait(false);
            MimeMessage msg;
            try { msg = await folder.GetMessageAsync(new UniqueId(uid)).ConfigureAwait(false); }
            catch { msg = null!; }
            if (msg is null) { await CierreAsync(cliente).ConfigureAwait(false); return null; }

            var adjuntos = new List<AdjuntoInfo>();
            int i = 0;
            foreach (var a in msg.Attachments)
                adjuntos.Add(new AdjuntoInfo(NombreAdjunto(a) ?? $"adjunto-{i + 1}", i++));

            var inline = new List<AdjuntoInfo>();
            i = 0;
            foreach (var p in PartesInline(msg))
                inline.Add(new AdjuntoInfo(NombreAdjunto(p) ?? $"imagen-{i + 1}", i++, p.ContentId ?? ""));

            var detalle = new MensajeDetalle(
                uid,
                msg.From?.Mailboxes.FirstOrDefault()?.Address ?? msg.From?.ToString() ?? "(sin remitente)",
                msg.To?.ToString() ?? "",
                string.IsNullOrWhiteSpace(msg.Subject) ? "(sin asunto)" : msg.Subject,
                msg.Date,
                msg.TextBody ?? "",
                msg.HtmlBody ?? "",
                adjuntos,
                inline,
                msg.Cc?.Mailboxes.Select(m => m.Address).ToList() ?? new List<string>(),
                msg.Bcc?.Mailboxes.Select(m => m.Address).ToList() ?? new List<string>());
            await CierreAsync(cliente).ConfigureAwait(false);
            return detalle;
        }
        catch { return null; }
    }

    /// Bytes de un adjunto por su indice (el que devolvio <see cref="DetalleAsync"/>).
    public Task<ArchivoAdjunto?> AdjuntoAsync(CorreoImapOptions o, uint uid, int indice, string? carpeta = null) =>
        ParteAsync(o, uid, indice, inline: false, carpeta);

    /// Bytes de una imagen incrustada en el cuerpo HTML (indice de la coleccion `inline`).
    public Task<ArchivoAdjunto?> InlineAsync(CorreoImapOptions o, uint uid, int indice, string? carpeta = null) =>
        ParteAsync(o, uid, indice, inline: true, carpeta);

    /// Partes del mensaje que van "inline" (imagenes referenciadas por cid dentro del cuerpo).
    /// Se decide por la cabecera Content-Disposition, no por `IsInline`, para no depender de la
    /// version de MimeKit: vale el disposition inline o cualquier imagen con ContentId.
    private static List<MimePart> PartesInline(MimeMessage msg) =>
        msg.BodyParts.OfType<MimePart>()
            .Where(p => string.Equals(p.ContentDisposition?.Disposition, ContentDisposition.Inline,
                           StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(p.ContentId) && p.ContentType.MimeType.StartsWith("image/")))
            .ToList();

    private static async Task<ArchivoAdjunto?> ParteAsync(CorreoImapOptions o, uint uid, int indice, bool inline, string? carpeta)
    {
        if (!EstaConfigurado(o) || indice < 0) return null;
        try
        {
            var (cliente, folder) = await ConectarLecturaAsync(o, carpeta, FolderAccess.ReadOnly).ConfigureAwait(false);
            MimeMessage msg;
            try { msg = await folder.GetMessageAsync(new UniqueId(uid)).ConfigureAwait(false); }
            catch { msg = null!; }
            if (msg is null) { await CierreAsync(cliente).ConfigureAwait(false); return null; }

            var lista = inline ? PartesInline(msg) : msg.Attachments.OfType<MimePart>().ToList();
            if (indice >= lista.Count) { await CierreAsync(cliente).ConfigureAwait(false); return null; }
            if (lista[indice] is not MimePart parte) { await CierreAsync(cliente).ConfigureAwait(false); return null; }

            using var ms = new MemoryStream();
            if (parte.Content is null) { await CierreAsync(cliente).ConfigureAwait(false); return null; }
            await parte.Content.DecodeToAsync(ms).ConfigureAwait(false);
            var archivo = new ArchivoAdjunto(ms.ToArray(), parte.ContentType?.MimeType ?? "application/octet-stream",
                NombreAdjunto(parte) ?? "adjunto");
            await CierreAsync(cliente).ConfigureAwait(false);
            return archivo;
        }
        catch { return null; }
    }

    /// Responde al remitente original con texto plano (endpoint antiguo, conservado por
    /// compatibilidad). Para responder con formato, imagenes o CC/CCO se usa <see cref="EnviarAsync"/>.
    public async Task<string?> ResponderAsync(CorreoImapOptions o, uint uid, string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "Escribe el mensaje de la respuesta antes de enviar.";
        if (!EstaConfigurado(o)) return "El buzon no esta configurado.";
        var r = await DetalleAsync(o, uid).ConfigureAwait(false);
        if (r is null) return "El mensaje original ya no esta en la bandeja.";
        return await EnviarAsync(o, new CorreoEnviarRequest(
            Para: new[] { r.Remitente }, Cc: null, Cco: null, Asunto: r.Asunto, Html: null,
            Texto: texto, EnRespuestaA: uid)).ConfigureAwait(false);
    }

    /// Envía un mensaje por SMTP con el buzon indicado: destinatarios visibles (Para/CC), copias
    /// ocultas (CCO, solo en el sobre), cuerpo enriquecido con imagenes incrustadas (cid), adjuntos,
    /// encadenamiento de la conversacion (In-Reply-To/References) y, si se pide, el mensaje original
    /// adjunto (reenviar). Devuelve null si todo bien o un mensaje de error legible y sin contrasenas.
    /// Al enviar con exito guarda una copia en la carpeta de Enviados (si el buzon la tiene).
    public async Task<string?> EnviarAsync(CorreoImapOptions o, CorreoEnviarRequest r)
    {
        if (!EstaConfigurado(o)) return "El buzon no esta configurado: pide al administrador que te enlace un correo en Usuarios.";

        var (mensaje, error) = await ConstruirMensajeAsync(o, r).ConfigureAwait(false);
        if (error is not null) return error;
        if (mensaje is null) return "No se pudo preparar el mensaje.";

        try
        {
            var hostSmtp = string.IsNullOrWhiteSpace(o.SmtpHost) ? o.Host : o.SmtpHost;
            using var smtp = new SmtpClient { Timeout = TimeoutMs(o) };
            await smtp.ConnectAsync(hostSmtp, o.SmtpPort, SslSmtp(o)).ConfigureAwait(false);
            await smtp.AuthenticateAsync(o.Usuario, o.Contrasena).ConfigureAwait(false);
            await smtp.SendAsync(mensaje).ConfigureAwait(false);
            try { await smtp.DisconnectAsync(true).ConfigureAwait(false); } catch { /* cierre cordial */ }
        }
        catch (Exception e)
        {
            return "No se pudo enviar: " + e.Message;
        }

        // Copia a Enviados: es un extra; si el buzon no tiene la carpeta o falla el append, el correo
        // ya se envio bien y no se devuelve error.
        try { await AnexarAsync(o, "enviados", mensaje).ConfigureAwait(false); } catch { }
        return null;
    }

    /// Guarda el mensaje como borrador en la carpeta de Borradores (IMAP APPEND con la marca \Draft),
    /// sin enviarlo. Reutiliza exactamente el mismo constructor que el envio, con sus imagenes
    /// incrustadas y adjuntos, para que al abrirlo de nuevo se vea igual. Devuelve null si se guardo
    /// o un mensaje legible si no (buzon sin carpeta de borradores, sin red, etc.).
    public async Task<string?> GuardarBorradorAsync(CorreoImapOptions o, CorreoEnviarRequest r)
    {
        if (!EstaConfigurado(o)) return "El buzon no esta configurado: pide al administrador que te enlace un correo en Usuarios.";

        // Un borrador admite cuerpo vacio y destinatarios vacios: es justo lo que se guarda a medias.
        var (mensaje, _) = await ConstruirMensajeAsync(o, r, comoBorrador: true).ConfigureAwait(false);
        if (mensaje is null) return "No se pudo preparar el borrador.";

        return await AnexarAsync(o, "borradores", mensaje, MessageFlags.Draft).ConfigureAwait(false);
    }

    /// Borra un mensaje de la carpeta indicada (se usa para eliminar borradores): marca \\Deleted y
    /// purga. Devuelve null si se elimino o un mensaje legible si no.
    public async Task<string?> EliminarAsync(CorreoImapOptions o, uint uid, string? carpeta)
    {
        if (!EstaConfigurado(o)) return "El buzon no esta configurado.";
        ImapClient? cliente = null;
        try
        {
            var (c, folder) = await ConectarLecturaAsync(o, carpeta, FolderAccess.ReadWrite).ConfigureAwait(false);
            cliente = c;
            await folder.AddFlagsAsync(new UniqueId(uid), MessageFlags.Deleted, true).ConfigureAwait(false);
            await folder.ExpungeAsync().ConfigureAwait(false);
            await CierreAsync(cliente).ConfigureAwait(false);
            cliente = null;
            return null;
        }
        catch (CarpetaNoEncontrada e) { return MensajeFaltaCarpeta(e.Message); }
        catch (Exception e) { return "No se pudo eliminar: " + e.Message; }
        finally { if (cliente is not null) { try { cliente.Disconnect(true); } catch { } } }
    }

    /// Arma el MimeMessage completo a partir de la peticion (destinatarios, CC/CCO, asunto con su
    /// prefijo RE:/RV:, conversacion, cuerpo con imagenes cid, adjuntos y original si se reenvia).
    /// Lo comparten <see cref="EnviarAsync"/> y <see cref="GuardarBorradorAsync"/>. Devuelve el mensaje
    /// o un error legible. Con <paramref name="comoBorrador"/> se relajan los requisitos minimos
    /// (puede guardarse sin destinatarios o sin cuerpo).
    private async Task<(MimeMessage? Mensaje, string? Error)> ConstruirMensajeAsync(
        CorreoImapOptions o, CorreoEnviarRequest r, bool comoBorrador = false)
    {
        var html = SanearHtml(r.Html);
        var texto = (r.Texto ?? "").Trim();
        if (!comoBorrador && string.IsNullOrWhiteSpace(html) && string.IsNullOrWhiteSpace(texto))
            return (null, "Escribe el mensaje antes de enviar.");

        MimeMessage? original = null;
        if (r.EnRespuestaA is > 0)
        {
            try
            {
                var (cliente, carpeta) = await ConectarLecturaAsync(o).ConfigureAwait(false);
                try { original = await carpeta.GetMessageAsync(new UniqueId((uint)r.EnRespuestaA!.Value)).ConfigureAwait(false); }
                finally { await CierreAsync(cliente).ConfigureAwait(false); }
            }
            catch { original = null; }
            if (original is null) return (null, "El mensaje original ya no esta en la bandeja.");
        }

        var mensaje = new MimeMessage();
        var nombre = string.IsNullOrWhiteSpace(o.RemitenteNombre) ? "Soporte" : o.RemitenteNombre;
        mensaje.From.Add(new MailboxAddress(nombre, o.Usuario));

        var para = new List<string>(r.Para ?? Array.Empty<string>());
        if (r.ResponderTodos && original is not null)
        {
            foreach (var d in original.To.Mailboxes.Select(m => m.Address)) para.Add(d);
            foreach (var d in original.Cc.Mailboxes.Select(m => m.Address)) para.Add(d);
        }
        var destinatarios = Distintar(para);
        if (destinatarios.Count == 0 && !comoBorrador)
            return (null, "Indica al menos un destinatario en «Para».");
        foreach (var d in destinatarios) mensaje.To.Add(new MailboxAddress("", d));

        foreach (var d in Distintar(r.Cc ?? Array.Empty<string>())) mensaje.Cc.Add(new MailboxAddress("", d));
        var cco = Distintar(r.Cco ?? Array.Empty<string>());
        foreach (var d in cco) mensaje.Bcc.Add(new MailboxAddress("", d));

        var asunto = (r.Asunto ?? "").Trim();
        if (original is not null && string.IsNullOrEmpty(asunto)) asunto = original.Subject ?? "";
        if (asunto.Length == 0) asunto = "(sin asunto)";
        var prefijo = r.IncluirOriginal ? "RV: " : "RE: ";
        if (original is not null && !asunto.StartsWith("RE:", StringComparison.OrdinalIgnoreCase)
                                   && !asunto.StartsWith("RV:", StringComparison.OrdinalIgnoreCase))
            asunto = prefijo + asunto;
        mensaje.Subject = asunto.Length > 500 ? asunto[..500] : asunto;

        if (original is not null)
        {
            if (!string.IsNullOrEmpty(original.MessageId))
            {
                mensaje.InReplyTo = original.MessageId;
                foreach (var refId in original.References) mensaje.References.Add(refId);
                mensaje.References.Add(original.MessageId);
            }
        }

        // ---- cuerpo ----
        var adjuntos = (r.Adjuntos ?? Array.Empty<AdjuntoBase64>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Nombre)).ToList();
        var imagenes = adjuntos.Where(a => !string.IsNullOrWhiteSpace(a.Cid)).ToList();
        var archivos = adjuntos.Where(a => string.IsNullOrWhiteSpace(a.Cid)).ToList();

        var cuerpo = string.IsNullOrWhiteSpace(html) ? TextoPlano(texto, html) : html;
        if (string.IsNullOrWhiteSpace(cuerpo)) cuerpo = texto;

        // 1) texto + HTML (el HTML manda; el plano es la alternativa para clientes sin formato)
        MimeEntity contenido;
        var htmlParte = new TextPart("html") { Text = cuerpo };
        if (!string.IsNullOrWhiteSpace(texto) && texto != cuerpo)
        {
            var alterno = new Multipart("alternative");
            alterno.Add(new TextPart("plain") { Text = texto });
            alterno.Add(htmlParte);
            contenido = alterno;
        }
        else contenido = htmlParte;

        // 2) imagenes incrustadas: van en un multipart/related con su cid para verse dentro del cuerpo
        if (imagenes.Count > 0)
        {
            var related = new Multipart("related");
            related.Add(contenido);
            foreach (var img in imagenes)
            {
                var bytes = Decodificar(img.Base64);
                if (bytes is null) return (null, "No se pudo leer una de las imagenes insertadas (" + img.Nombre + ").");
                if (bytes.Length > AdjuntoMaxBytes)
                    return (null, "La imagen " + img.Nombre + " pesa mas de " + (AdjuntoMaxBytes / 1024 / 1024) + " MB.");
                related.Add(ParteImagen(img, bytes));
            }
            contenido = related;
        }

        // 3) archivos adjuntos y, en el reenvio, el mensaje original como adjunto .eml
        var incluyeOriginal = r.IncluirOriginal && original is not null;
        if (archivos.Count > 0 || incluyeOriginal)
        {
            var mixto = new Multipart("mixed");
            mixto.Add(contenido);
            foreach (var a in archivos)
            {
                var bytes = Decodificar(a.Base64);
                if (bytes is null) return (null, "No se pudo leer el adjunto " + a.Nombre + ".");
                if (bytes.Length > AdjuntoMaxBytes)
                    return (null, "El adjunto " + a.Nombre + " pesa mas de " + (AdjuntoMaxBytes / 1024 / 1024) + " MB; quítalo o usa un archivo mas pequeno.");
                mixto.Add(ParteAdjunto(a, bytes));
            }
            if (incluyeOriginal)
                mixto.Add(new MessagePart("message", "rfc822") { Message = original! });
            contenido = mixto;
        }

        mensaje.Body = contenido;
        return (mensaje, null);
    }

    private static MimePart ParteImagen(AdjuntoBase64 a, byte[] bytes)
    {
        var parte = new MimePart(a.ContentType.StartsWith("image/") ? a.ContentType : "image/png")
        {
            Content = new MimeContent(new MemoryStream(bytes)),
            FileName = a.Nombre,
            ContentId = a.Cid,
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
        };
        return parte;
    }

    private static MimePart ParteAdjunto(AdjuntoBase64 a, byte[] bytes)
    {
        var tipo = a.ContentType.Contains('/') ? a.ContentType : "application/octet-stream";
        return new MimePart(tipo)
        {
            Content = new MimeContent(new MemoryStream(bytes)),
            FileName = a.Nombre,
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
        };
    }

    /// Cuerpo en texto plano cuando el editor no produce HTML (o como alternativa para clientes
    /// que no muestran HTML).
    private static string TextoPlano(string texto, string? html) =>
        !string.IsNullOrWhiteSpace(texto) ? texto : (html is null ? "" : Regex.Replace(html, "<[^>]+>", ""));

    /// Quita duplicados conservando el orden, ignora vacios y normaliza en minusculas la direccion.
    private static List<string> Distintar(IEnumerable<string> direcciones)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var salida = new List<string>();
        foreach (var bruto in direcciones)
        {
            foreach (var parte in (bruto ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var d = ExtraerDireccion(parte);
                if (d.Length > 0 && d.Length <= 254 && d.Contains('@') && vistos.Add(d)) salida.Add(d);
            }
        }
        return salida;
    }

    /// Acepta "Juan García <juan@x.mx>" o "juan@x.mx" y devuelve solo la direccion, en minusculas.
    private static string ExtraerDireccion(string texto)
    {
        var t = (texto ?? "").Trim();
        var inicio = t.IndexOf('<');
        var fin = t.IndexOf('>', inicio + 1);
        if (inicio >= 0 && fin > inicio) t = t[(inicio + 1)..fin];
        return t.Trim();
    }

    private static readonly Regex Script = new(@"<\s*(script|iframe|object|embed|form|link|meta|style)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ScriptAbierto = new(@"<\s*(script|iframe|object|embed|form|link|meta|style|base)\b[^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Cerrado = new(@"<\s*/\s*(script|iframe|object|embed|form|style)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Eventos = new(@"\son[a-z]+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UrlPeligrosa = new(@"(href|src)\s*=\s*(?:""|\')?\s*(?:javascript|vbscript|data\s*:\s*text/html)\s*:[^""'\s>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// Desinfeccion minima del HTML que llega del editor: se quitan etiquetas que ejecutan codigo
    /// (script/iframe/object/form…), los manejadores `on*` y las URL javascript:. Se conservan las
    /// imagenes en data: y los `cid:` de las imagenes incrustadas, que son justo lo que se quiere mandar.
    public static string SanearHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = Script.Replace(html, "");
        s = ScriptAbierto.Replace(s, "");
        s = Cerrado.Replace(s, "");
        s = Eventos.Replace(s, "");
        s = UrlPeligrosa.Replace(s, "$1=\"\"");
        return s.Trim();
    }

    private static byte[]? Decodificar(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            var s = base64.Trim();
            var coma = s.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (coma >= 0) s = s[(coma + 7)..];
            s = s.Replace("\n", "").Replace("\r", "").Replace(" ", "");
            return Convert.FromBase64String(s);
        }
        catch { return null; }
    }

    private static string? NombreAdjunto(MimeEntity a) =>
        (a as MimePart)?.FileName ?? (a as MessagePart)?.ContentDisposition?.FileName;

    /// Cita el cuerpo en texto del mensaje original, al estilo de un cliente de correo.
    public static string Citar(string remitente, DateTimeOffset fecha, string texto)
    {
        var sb = new StringBuilder();
        sb.Append("---- Mensaje original ----\n");
        sb.Append(fecha.ToString("dd/MM/yyyy HH:mm")).Append(" · ").Append(remitente).Append(":\n");
        foreach (var linea in (texto ?? "").Replace("\r\n", "\n").Split('\n'))
            sb.Append("> ").Append(linea).Append('\n');
        return sb.ToString();
    }
}
