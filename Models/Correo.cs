namespace Meditechnosys.Intranet.Models;

/// Datos de conexion al buzon de soporte. Se enlazan desde la seccion <c>Correo:Imap</c> de
/// appsettings.json. La contrasena NUNCA se escribe en el codigo, en los registros ni en la
/// documentacion: vive solo en la configuracion del servidor y la coloca ahi el administrador.
/// Las mismas credenciales sirven para IMAP (lectura) y SMTP (envio de respuestas).
public sealed class CorreoImapOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public string Usuario { get; set; } = "";
    public string Contrasena { get; set; } = "";
    /// true = SSL implicito (puerto 993). Si el servidor usara STARTTLS, poner false.
    public bool Ssl { get; set; } = true;
    /// Servidor de salida para responder (SMTP). Si se deja vacio se reusa <see cref="Host"/>.
    public string SmtpHost { get; set; } = "";
    /// Puerto SMTP con SSL implicito (tipicamente 465).
    public int SmtpPort { get; set; } = 465;
    public bool SmtpSsl { get; set; } = true;
    /// Nombre visible del remitente al responder.
    public string RemitenteNombre { get; set; } = "Soporte Meditechnosys";
    /// Carpeta a leer; por defecto la bandeja de entrada.
    public string Carpeta { get; set; } = "INBOX";
    /// Cuantos mensajes recientes devolver como maximo.
    public int Maximo { get; set; } = 50;
    /// Tope de espera por operacion, para no bloquear la pagina si el servidor no responde.
    public int TimeoutSegundos { get; set; } = 15;
}

/// Cabeceras de un mensaje de la bandeja (lo minimo para listarla sin descargar cuerpos).
/// <see cref="Uid"/> es el identificador estable del mensaje en el servidor IMAP: es el que se
/// usa para abrirlo, descargar adjuntos y responder. <see cref="ConAdjuntos"/> queda false en la
/// lectura por IMAP: la cabecera no informa de adjuntos de forma fiable, asi que la pantalla los
/// muestra al abrir el mensaje.
public record MensajeCorreo(uint Uid, string Remitente, string Asunto, DateTimeOffset Fecha, bool ConAdjuntos = false);

/// Resultado de leer la bandeja: si el buzon esta configurado, los mensajes y un mensaje de
/// error legible (nunca datos sensibles) cuando algo fallo o falta por configurar.
public record ResultadoCorreo(bool Configurado, IReadOnlyList<MensajeCorreo> Mensajes, string? Error = null);

/// Un adjunto de un mensaje (nombre e indice para poder descargarlo despues).
/// <see cref="Cid"/> es el identificador interno de las imagenes que van dentro del cuerpo HTML
/// (src="cid:..."); vacio en los adjuntos normales.
public record AdjuntoInfo(string Nombre, int Indice, string Cid = "");

/// Contenido completo de un mensaje para abrirlo: cabeceras, cuerpo en texto y en HTML y adjuntos.
/// <see cref="Inline"/> son las partes incrustadas (imagenes del cuerpo), que se sirven por otro
/// indice para no mezclarlas con los adjuntos descargables. <see cref="CopiasOcultas"/> es el CCO
/// guardado en el propio borrador: sirve para reabrir un borrador con sus copias ocultas intactas.
public record MensajeDetalle(uint Uid, string Remitente, string Destinatarios, string Asunto,
    DateTimeOffset Fecha, string Texto, string Html, IReadOnlyList<AdjuntoInfo> Adjuntos,
    IReadOnlyList<AdjuntoInfo>? Inline = null, IReadOnlyList<string>? Copias = null,
    IReadOnlyList<string>? CopiasOcultas = null);

/// Bytes de un adjunto listo para servirse como descarga.
public record ArchivoAdjunto(byte[] Bytes, string ContentType, string Nombre);

/// Cuerpo de la peticion de respuesta (se conserva por compatibilidad con la pantalla antigua:
/// el envio completo va por /api/correos/enviar).
public record RespuestaRequest(string? Mensaje);

/// Que buzon esta usando realmente la bandeja: el enlazado del usuario o, si no tiene, el de
/// soporte. La pantalla lo muestra en el banner para que siempre quede claro con que correo se
/// esta leyendo y se responde.
public record BuzonEnlazado(string Correo, bool EsPropio, bool HayEnlace);

/// Una de las dos cuentas que la bandeja ofrece: <c>soporte</c> (la de appsettings.json, que todos
/// tienen siempre) y <c>propio</c> (el segundo correo que el administrador enlaza en Usuarios).
/// `Id` es el valor que viaja en la peticion (`?cuenta=` o `cuenta` en el cuerpo). `Disponible` dice
/// si se puede abrir ya, y `Aviso` explica por que no, sin contrasenas ni datos sensibles.
public record CuentaBuzon(string Id, string Correo, string Nombre, bool EsSoporte, bool Disponible, string? Aviso, bool Enlazado);
