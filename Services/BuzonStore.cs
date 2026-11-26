using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// <summary>Cifra y descifra la contrasena del buzon de cada usuario con AES-256-GCM.
/// La clave simetrica es un archivo aleatorio de 32 bytes que se crea solo la primera vez que se
/// guarda un buzon: no esta en el codigo ni en el repositorio. Su ubicacion la decide
/// <c>Auth:BuzonKeyFile</c> (ruta absoluta, la que usa el servidor) y, si no viene configurada,
/// <c>App_Data/buzon-clave.bin</c> junto al ejecutable. Hay que fijarla en la configuracion porque
/// cada carpeta de salida (bin/Debug, bin/Release, publish) generaria la suya y las contrasenas
/// guardadas por una version dejarian de leerse al arrancar la otra.
/// Formato guardado: <c>g1.base64(nonce).base64(densificado+etiqueta)</c>.
/// Si la clave se pierde (se borro App_Data), lo unico que ocurre es que habra que escribir de
/// nuevo la contrasena del buzon: los demas datos de configuracion siguen sirviendo.</summary>
public sealed class BuzonCrypto
{
    private const string Prefijo = "g1";
    private readonly byte[] _clave;

    /// false cuando no se pudo leer ni crear el archivo y se usa una clave de esta ejecucion:
    /// en ese caso lo guardado moriria al reiniciar, asi que no se guarda nada.
    public bool ClavePermanente { get; }

    public BuzonCrypto(IWebHostEnvironment env, IConfiguration config, ILogger<BuzonCrypto> logger)
    {
        var ruta = config["Auth:BuzonKeyFile"]
            ?? Path.Combine(env.ContentRootPath, "App_Data", "buzon-clave.bin");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            if (File.Exists(ruta))
            {
                _clave = File.ReadAllBytes(ruta);
                if (_clave.Length != 32) throw new InvalidOperationException("la clave no tiene 32 bytes");
            }
            else
            {
                _clave = RandomNumberGenerator.GetBytes(32);
                File.WriteAllBytes(ruta, _clave);
            }
            ClavePermanente = true;
            logger.LogInformation("Clave de buzones: {Ruta}", ruta);
        }
        catch (Exception e)
        {
            // Sin clave en disco no se puede guardar nada duradero: se genera una en memoria para
            // esta ejecucion (la bandeja sigue leyendo lo que se pueda) y ClavePermanente lo impide.
            logger.LogWarning("No se pudo usar la clave de buzones {Ruta} ({Error}); se usa una clave temporal.", ruta, e.Message);
            _clave = RandomNumberGenerator.GetBytes(32);
            ClavePermanente = false;
        }
    }

    /// Devuelve el texto cifrado listo para guardarse, o null si no hay nada que cifrar.
    public string? Cifrar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return null;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var claro = Encoding.UTF8.GetBytes(texto);
        var denso = new byte[claro.Length];
        var etiqueta = new byte[16];
        using var aes = new AesGcm(_clave, 16);
        aes.Encrypt(nonce, claro, denso, etiqueta);
        // El base64 se arma con el densificado y la etiqueta UNIDOS, en una sola llamada. Si cada
        // uno se codifica aparte y se pegan los textos, el '=' de relleno del primero cae en medio
        // del valor y Convert.FromBase64String ya no puede leerlo: toda contrasena cuyo largo no era
        // multiplo de 3 se guardaba ilegible y la bandeja avisaba de que "perdio su contrasena".
        var todo = new byte[denso.Length + etiqueta.Length];
        denso.CopyTo(todo, 0);
        etiqueta.CopyTo(todo, denso.Length);
        return $"{Prefijo}.{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(todo)}";
    }

    /// Descifra lo guardado. Null si el valor esta vacio, es de otro formato o la clave cambio
    /// (en ese caso el usuario debera volver a escribir su contrasena, nunca se falla la pagina).
    public string? Descifrar(string? cifrado)
    {
        if (string.IsNullOrWhiteSpace(cifrado)) return null;
        try
        {
            var partes = cifrado.Split('.');
            if (partes.Length != 3 || partes[0] != Prefijo) return null;
            var nonce = Convert.FromBase64String(partes[1]);
            var todo = Convert.FromBase64String(partes[2]);
            if (nonce.Length != 12 || todo.Length < 16) return null;
            var denso = todo[..^16];
            var etiqueta = todo[^16..];
            var claro = new byte[denso.Length];
            using var aes = new AesGcm(_clave, 16);
            aes.Decrypt(nonce, denso, etiqueta, claro);
            return Encoding.UTF8.GetString(claro);
        }
        catch
        {
            return null;
        }
    }

    /// true si el valor tiene el formato de esta aplicacion (prefijo g1). Si no lo tiene, lo
    /// guardo una version anterior o se escribio a mano: hay que reescribir la contrasena.
    public static bool PareceCifrado(string? texto) =>
        !string.IsNullOrWhiteSpace(texto) && texto.StartsWith(Prefijo + ".", StringComparison.Ordinal);

    /// true si el valor sale del empaque defectuoso de una version anterior: dos base64 pegados, con
    /// su '=' de relleno caido en medio del payload. Un base64 bien formado solo lleva relleno al
    /// final, asi que un '=' seguido de mas caracteres delata ese formato viejo, que no se puede leer
    /// ni con la clave correcta: unicas salidas, reescribir la contrasena o volver a guardarla.
    public static bool EsEmpaqueAntiguo(string? texto)
    {
        if (!PareceCifrado(texto)) return false;
        var partes = texto!.Split('.');
        if (partes.Length != 3) return false;
        var relleno = partes[2].IndexOf('=');
        // El relleno legitimo son uno o dos '=' al final; si despues del primero quedan mas de dos
        // caracteres, es el segundo base64 pegado detras (el empaque viejo).
        return relleno >= 0 && partes[2].Length - relleno > 2;
    }
}

/// <summary>Acceso al buzon enlazado de cada usuario. Guarda correo, servidores y la contrasena
/// cifrada; no lanza excepciones: un fallo de almacenamiento se traduce a un mensaje legible.</summary>
public interface IBuzonStore
{
    BuzonGuardado? Obtener(string usuario);
    IReadOnlyList<BuzonGuardado> Todos();
    /// <returns>204 guardado o limpiado, 404 si el usuario no existe, 400 si los datos no son validos.</returns>
    (int Status, string? Error) Guardar(string usuario, BuzonRequest r, string? passwordPlano, BuzonGuardado? previo);
    /// <summary>Elimina el buzon cuando se da de baja al usuario.</summary>
    void Eliminar(string usuario);
}

/// Validaciones comunes a los dos almacenes (JSON y MySQL). Devuelve el error legible o null.
public static class BuzonRules
{
    public const int HostMax = 120;
    public const int CorreoMax = 160;

    public static string? Error(BuzonRequest r)
    {
        var correo = (r.Correo ?? "").Trim();
        if (correo.Length == 0) return "Escribe el correo del buzon (p. ej. jmendez@meditechnosys.mx).";
        if (correo.Length > CorreoMax) return $"El correo no puede pasar de {CorreoMax} caracteres.";
        if (!correo.Contains('@') || correo.StartsWith('@') || correo.EndsWith('@'))
            return "El correo del buzon no tiene un formato valido (falta la @).";
        if ((r.ImapHost ?? "").Trim().Length == 0)
            return "Indica el servidor de entrada (IMAP), p. ej. mail.meditechnosys.mx.";
        if ((r.SmtpHost ?? "").Trim().Length == 0)
            return "Indica el servidor de salida (SMTP), p. ej. mail.meditechnosys.mx.";
        var puertoI = r.ImapPort ?? 993;
        var puertoS = r.SmtpPort ?? 465;
        if (puertoI is < 1 or > 65535) return "El puerto IMAP debe estar entre 1 y 65535.";
        if (puertoS is < 1 or > 65535) return "El puerto SMTP debe estar entre 1 y 65535.";
        return null;
    }

    public static string Limpio(string? texto, int max) =>
        UserRules.LimpiarTexto(texto, max);
}

/// <summary>Almacen en archivo JSON (<c>App_Data/buzones.json</c>), el mismo patron que el resto
/// de los almacenes de respaldo de la intranet. La contrasena se guarda ya cifrada.</summary>
public sealed class JsonBuzonStore : IBuzonStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _lock = new();
    private readonly string _ruta;
    private readonly Dictionary<string, BuzonGuardado> _datos;

    public JsonBuzonStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonBuzonStore> logger)
    {
        _ruta = config["Auth:BuzonFile"] ?? Path.Combine(env.ContentRootPath, "App_Data", "buzones.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_ruta)!);
        var lista = new List<BuzonGuardado>();
        if (File.Exists(_ruta))
        {
            try { lista = JsonSerializer.Deserialize<List<BuzonGuardado>>(File.ReadAllText(_ruta), Json) ?? new(); }
            catch (Exception e) { logger.LogWarning("No se pudo leer buzones.json: {Error}", e.Message); }
        }
        _datos = lista
            .GroupBy(b => b.Usuario, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public BuzonGuardado? Obtener(string usuario)
    {
        lock (_lock)
            return _datos.TryGetValue((usuario ?? "").Trim(), out var b) ? b : null;
    }

    public IReadOnlyList<BuzonGuardado> Todos()
    {
        lock (_lock) return _datos.Values.OrderBy(b => b.Usuario, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public (int Status, string? Error) Guardar(string usuario, BuzonRequest r, string? passwordPlano, BuzonGuardado? previo)
    {
        usuario = (usuario ?? "").Trim();
        if (r.Limpiar) { Eliminar(usuario); return (204, null); }
        var cifrada = passwordPlano ?? previo?.PasswordCifrada ?? "";
        if (string.IsNullOrEmpty(cifrada)) return (400, "Falta la contrasena del buzon.");

        var nuevo = Construir(usuario, r, cifrado: cifrada, previo: previo);
        lock (_lock)
        {
            _datos[usuario] = nuevo;
            var tmp = _ruta + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_datos.Values.ToList(), Json));
            File.Move(tmp, _ruta, overwrite: true);
        }
        return (204, null);
    }

    public void Eliminar(string usuario)
    {
        lock (_lock)
        {
            if (_datos.Remove((usuario ?? "").Trim()))
                File.WriteAllText(_ruta, JsonSerializer.Serialize(_datos.Values.ToList(), Json));
        }
    }

    internal static BuzonGuardado Construir(string usuario, BuzonRequest r, string cifrado, BuzonGuardado? previo) => new(
        usuario,
        BuzonRules.Limpio(r.Correo, BuzonRules.CorreoMax),
        cifrado,
        BuzonRules.Limpio(r.ImapHost, BuzonRules.HostMax),
        Math.Clamp(r.ImapPort ?? 993, 1, 65535),
        r.ImapSsl ?? previo?.ImapSsl ?? true,
        BuzonRules.Limpio(r.SmtpHost, BuzonRules.HostMax),
        Math.Clamp(r.SmtpPort ?? 465, 1, 65535),
        r.SmtpSsl ?? previo?.SmtpSsl ?? true,
        BuzonRules.Limpio(string.IsNullOrWhiteSpace(r.RemitenteNombre) ? (previo?.RemitenteNombre ?? "") : r.RemitenteNombre, 120),
        DateTime.UtcNow);
}

/// <summary>Almacen en MySQL (tabla <c>usuario_buzones</c>), con la misma semantica que el JSON.
/// La columna <c>password</c> guarda el texto cifrado (prefijo g1 + base64), jamas la contrasena
/// en claro. La tabla se crea sola en el primer arranque (EnsureSchema).</summary>
public sealed class MySqlBuzonStore : IBuzonStore
{
    private readonly string _cs;

    public MySqlBuzonStore(IConfiguration config)
    {
        _cs = config.GetConnectionString("Usuarios")
              ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");
    }

    private MySqlConnection Conexion() => new(_cs);

    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        if (string.IsNullOrEmpty(db))
            throw new InvalidOperationException("La cadena ConnectionStrings:Usuarios debe incluir Database.");
        using var cn = Conexion();
        cn.Open();
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS `{db}`.`usuario_buzones` (
                  `usuario`          VARCHAR(40)  NOT NULL PRIMARY KEY,
                  `correo`           VARCHAR(160) NOT NULL DEFAULT '',
                  `password`         TEXT         NOT NULL,
                  `imap_host`        VARCHAR(120) NOT NULL DEFAULT '',
                  `imap_port`        INT          NOT NULL DEFAULT 993,
                  `imap_ssl`         TINYINT(1)   NOT NULL DEFAULT 1,
                  `smtp_host`        VARCHAR(120) NOT NULL DEFAULT '',
                  `smtp_port`        INT          NOT NULL DEFAULT 465,
                  `smtp_ssl`         TINYINT(1)   NOT NULL DEFAULT 1,
                  `remitente_nombre` VARCHAR(120) NOT NULL DEFAULT '',
                  `actualizado`      DATETIME(6)  NOT NULL
                ) ENGINE=InnoDB;
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public BuzonGuardado? Obtener(string usuario)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = Seleccion + " WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
            using var r = cmd.ExecuteReader();
            return r.Read() ? Leer(r) : null;
        }
        catch { return null; }
    }

    public IReadOnlyList<BuzonGuardado> Todos()
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = Seleccion + " ORDER BY usuario";
            var lista = new List<BuzonGuardado>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) lista.Add(Leer(r));
            return lista;
        }
        catch { return Array.Empty<BuzonGuardado>(); }
    }

    public (int Status, string? Error) Guardar(string usuario, BuzonRequest r, string? passwordPlano, BuzonGuardado? previo)
    {
        usuario = (usuario ?? "").Trim();
        if (r.Limpiar) { Eliminar(usuario); return (204, null); }
        var cifrada = passwordPlano ?? previo?.PasswordCifrada ?? "";
        if (string.IsNullOrEmpty(cifrada)) return (400, "Falta la contrasena del buzon.");
        var nuevo = JsonBuzonStore.Construir(usuario, r, cifrado: cifrada, previo: previo);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO usuario_buzones
                  (usuario, correo, password, imap_host, imap_port, imap_ssl,
                   smtp_host, smtp_port, smtp_ssl, remitente_nombre, actualizado)
                VALUES (@u, @c, @p, @ih, @ip, @is, @sh, @sp, @ss, @rn, @au)
                ON DUPLICATE KEY UPDATE
                  correo = @c, password = @p, imap_host = @ih, imap_port = @ip, imap_ssl = @is,
                  smtp_host = @sh, smtp_port = @sp, smtp_ssl = @ss, remitente_nombre = @rn, actualizado = @au;
                """;
            cmd.Parameters.AddWithValue("@u", nuevo.Usuario);
            cmd.Parameters.AddWithValue("@c", nuevo.Correo);
            cmd.Parameters.AddWithValue("@p", nuevo.PasswordCifrada);
            cmd.Parameters.AddWithValue("@ih", nuevo.ImapHost);
            cmd.Parameters.AddWithValue("@ip", nuevo.ImapPort);
            cmd.Parameters.AddWithValue("@is", nuevo.ImapSsl);
            cmd.Parameters.AddWithValue("@sh", nuevo.SmtpHost);
            cmd.Parameters.AddWithValue("@sp", nuevo.SmtpPort);
            cmd.Parameters.AddWithValue("@ss", nuevo.SmtpSsl);
            cmd.Parameters.AddWithValue("@rn", nuevo.RemitenteNombre);
            cmd.Parameters.AddWithValue("@au", nuevo.Actualizado);
            cmd.ExecuteNonQuery();
            return (204, null);
        }
        catch { return (500, "No se pudo guardar el buzon en la base de datos."); }
    }

    public void Eliminar(string usuario)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM usuario_buzones WHERE usuario = @u";
            cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
            cmd.ExecuteNonQuery();
        }
        catch { /* baja de un usuario sin buzon: no hay nada que borrar */ }
    }

    private const string Seleccion =
        "SELECT usuario, correo, password, imap_host, imap_port, imap_ssl, smtp_host, smtp_port, smtp_ssl, remitente_nombre, actualizado FROM usuario_buzones";

    private static BuzonGuardado Leer(MySqlDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.GetInt32(4), r.GetBoolean(5), r.GetString(6), r.GetInt32(7), r.GetBoolean(8),
        r.GetString(9), DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc));
}

/// <summary>Traduce el buzon guardado de un usuario a las opciones que usa <see cref="CorreoService"/>.
/// Los campos que el formulario no toca (carpeta, maximo, tiempo de espera) se heredan del buzón
/// de soporte para que el comportamiento de lectura siga siendo el ya probado.</summary>
public static class BuzonOpciones
{
    /// <summary>Opciones de conexion a partir del buzon guardado. <paramref name="contrasena"/> es la
    /// contrasena YA DESCIFRADA en memoria (la pone el llamador con <see cref="BuzonCrypto.Descifrar"/>);
    /// nunca se baja el texto cifrado al cliente ni se escribe en la bitacora.</summary>
    public static CorreoImapOptions Desde(CorreoImapOptions predeterminado, BuzonGuardado b, string contrasena)
    {
        return new CorreoImapOptions
        {
            Host = b.ImapHost,
            Port = b.ImapPort,
            Usuario = b.Correo,
            Contrasena = contrasena,
            Ssl = b.ImapSsl,
            SmtpHost = b.SmtpHost,
            SmtpPort = b.SmtpPort,
            SmtpSsl = b.SmtpSsl,
            RemitenteNombre = string.IsNullOrWhiteSpace(b.RemitenteNombre) ? b.Correo : b.RemitenteNombre,
            Carpeta = predeterminado.Carpeta,
            Maximo = predeterminado.Maximo,
            TimeoutSegundos = predeterminado.TimeoutSegundos,
        };
    }

    /// Copia de las opciones globales (buzón de soporte) para no compartirlas con el registro de DI.
    public static CorreoImapOptions Copiar(CorreoImapOptions o) => new()
    {
        Host = o.Host,
        Port = o.Port,
        Usuario = o.Usuario,
        Contrasena = o.Contrasena,
        Ssl = o.Ssl,
        SmtpHost = o.SmtpHost,
        SmtpPort = o.SmtpPort,
        SmtpSsl = o.SmtpSsl,
        RemitenteNombre = o.RemitenteNombre,
        Carpeta = o.Carpeta,
        Maximo = o.Maximo,
        TimeoutSegundos = o.TimeoutSegundos,
    };

    /// Vista sin contrasena para devolverla al formulario. <see cref="BuzonPublico.ContrasenaLegible"/>
    /// dice si lo guardado se puede descifrar con la clave actual: si no, el aviso honesto es
    /// "vuelva a escribirla", no "ya esta guardada".
    public static BuzonPublico Publico(BuzonGuardado? b, BuzonCrypto? crypto = null) => b is null
        ? new BuzonPublico("", "", "", 993, true, "", 465, true, "", false, false, null)
        : new BuzonPublico(b.Usuario, b.Correo, b.ImapHost, b.ImapPort, b.ImapSsl,
            b.SmtpHost, b.SmtpPort, b.SmtpSsl, b.RemitenteNombre,
            !string.IsNullOrEmpty(b.PasswordCifrada),
            crypto is not null && crypto.Descifrar(b.PasswordCifrada) is not null,
            b.Actualizado);
}
