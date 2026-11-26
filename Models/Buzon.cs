namespace Meditechnosys.Intranet.Models;

/// <summary>Datos de conexion al buzon de un usuario (IMAP para leer, SMTP para enviar).
/// Se configura al dar de alta al usuario o al editarlo (/admin/usuarios); la bandeja no lo cambia.
/// La contrasena del buzon
/// NUNCA se guarda en claro: vive cifrada en el almacen (tabla <c>usuario_buzones</c> o
/// <c>App_Data/buzones.json</c>) y solo se descifra en memoria para conectar con el servidor.
/// Si un usuario no tiene buzon enlazado, la bandeja usa el buzón de soporte de
/// <c>appsettings.json</c> (seccion <c>Correo:Imap</c>).</summary>
public record BuzonGuardado(
    string Usuario,
    string Correo,
    string PasswordCifrada,
    string ImapHost,
    int ImapPort,
    bool ImapSsl,
    string SmtpHost,
    int SmtpPort,
    bool SmtpSsl,
    string RemitenteNombre,
    DateTime Actualizado);

/// <summary>Lo que envia el formulario (alta/edicion de usuario o modal de la bandeja).
/// <see cref="MantenerContrasena"/> true = deja la contrasena ya guardada (se edita un usuario sin
/// escribirla de nuevo). <see cref="Limpiar"/> true = desvincula el buzon y el usuario vuelve a ver
/// el buzón de soporte.</summary>
public record BuzonRequest(
    string? Correo,
    string? Password,
    string? ImapHost,
    int? ImapPort,
    bool? ImapSsl,
    string? SmtpHost,
    int? SmtpPort,
    bool? SmtpSsl,
    string? RemitenteNombre,
    bool MantenerContrasena = false,
    bool Limpiar = false);

/// <summary>Vista publica de un buzon: todo menos la contrasena, que jamas se devuelve al
/// navegador. <see cref="TieneContrasena"/> permite al formulario decir "ya hay una guardada" y
/// <see cref="ContrasenaLegible"/> aclara si de verdad se puede usar con la clave actual del
/// servidor: si hay valor pero no es legible, hace falta reescribirla.</summary>
public record BuzonPublico(
    string Usuario,
    string Correo,
    string ImapHost,
    int ImapPort,
    bool ImapSsl,
    string SmtpHost,
    int SmtpPort,
    bool SmtpSsl,
    string RemitenteNombre,
    bool TieneContrasena,
    bool ContrasenaLegible,
    DateTime? Actualizado);

/// <summary>Archivo que acompana a un mensaje enviado desde la bandeja (nombre, contenido en
/// base64 y, en las imagenes insertadas en el cuerpo, el identificador <c>cid</c> con el que se
/// referencian para que se vean dentro del correo y no como adjunto suelto).</summary>
public record AdjuntoBase64(string Nombre, string ContentType, string Base64, string? Cid = null);

/// <summary>Parametros de un mensaje que se envia desde la bandeja: responder, responder a todos,
/// reenviar o redactar de cero. Todos los destinatarios son opcionales salvo <see cref="Para"/>,
/// y <see cref="Cco"/> se usa solo para el sobre SMTP (no se escribe en el correo enviado).</summary>
public record CorreoEnviarRequest(
    IReadOnlyList<string>? Para,
    IReadOnlyList<string>? Cc,
    IReadOnlyList<string>? Cco,
    string? Asunto,
    string? Html,
    string? Texto,
    /// Uid del mensaje al que se responde o que se reenvia (0 = mensaje nuevo).
    long? EnRespuestaA,
    /// true = el envio va como "responder a todos" (se incluyen los demas destinatarios del original).
    bool ResponderTodos = false,
    /// true = se adjunta el mensaje original (reenviar).
    bool IncluirOriginal = false,
    IReadOnlyList<AdjuntoBase64>? Adjuntos = null,
    /// Cuenta con la que se envia: <c>soporte</c> (por defecto) o <c>propio</c>, el segundo correo
    /// enlazado en Usuarios. Se ignora —y se usa soporte— si el usuario no tiene segundo correo.
    string? Cuenta = null);
