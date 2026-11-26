using System.Text.Json;
using System.Text.RegularExpressions;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Una credencial (gafete) ligada a una cuenta de la intranet (`Usuario`). Los datos que se
/// imprimen en el formato oficial son `Nombre`, `Puesto`, `Nss`, `Cedula` y `Vigencia`
/// (MM/AAAA). `FotoArchivo` es el nombre del archivo de la foto tamaño infantil dentro de
/// App_Data/credenciales/ (vacío si todavía no se ha subido).
public record Credencial(
    int Id,
    string Usuario,
    string Nombre,
    string Puesto,
    string Nss,
    string Cedula,
    string Vigencia,
    string FotoArchivo,
    string CreadoPor,
    DateTime Creado,
    DateTime Actualizado);

/// Reglas de los campos de una credencial (compartidas por la API y los almacenes).
public static class ReglasCredencial
{
    public const int NombreMin = 3;
    public const int NombreMax = 120;
    public const int PuestoMin = 3;
    public const int PuestoMax = 80;
    public const int NssMax = 20;
    public const int CedulaMax = 20;
    public static readonly Regex VigenciaOk = new(@"^(0[1-9]|1[0-2])/\d{4}$", RegexOptions.Compiled);

    /// Mensaje de error o null si los campos son aceptables.
    public static string? Validar(string? usuario, string? nombre, string? puesto, string? nss, string? cedula, string? vigencia)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            return "La credencial debe ligarse a un usuario de la intranet.";
        var n = (nombre ?? "").Trim();
        if (n.Length < NombreMin || n.Length > NombreMax)
            return $"El nombre impreso debe tener entre {NombreMin} y {NombreMax} caracteres.";
        var p = (puesto ?? "").Trim();
        if (p.Length < PuestoMin || p.Length > PuestoMax)
            return $"El puesto impreso debe tener entre {PuestoMin} y {PuestoMax} caracteres.";
        if ((nss ?? "").Trim().Length > NssMax)
            return $"El NSS no puede pasar de {NssMax} caracteres.";
        if ((cedula ?? "").Trim().Length > CedulaMax)
            return $"La cédula no puede pasar de {CedulaMax} caracteres.";
        var v = (vigencia ?? "").Trim();
        if (!VigenciaOk.IsMatch(v))
            return "La vigencia debe capturarse como MM/AAAA (por ejemplo 12/2028).";
        return null;
    }
}

/// Fotos tamaño infantil de las credenciales: viven como archivos en App_Data/credenciales/
/// tanto en modo MySQL como en modo JSON (son binarios, no registros). Aquí se valida el
/// archivo subido (tipo, peso y firma binaria) para no aceptar cualquier cosa con extensión de imagen.
public static class FotosCredencial
{
    public const long PesoMaximo = 2 * 1024 * 1024; // 2 MB

    public static string Directorio(IWebHostEnvironment env) =>
        Path.Combine(env.ContentRootPath, "App_Data", "credenciales");

    /// Mensaje de error o null si el archivo es una imagen aceptable (JPEG/PNG real).
    public static string? Validar(string? contentType, long length, byte[] bytes)
    {
        if (length <= 0 || bytes.Length == 0) return "La foto tamaño infantil es obligatoria.";
        if (length > PesoMaximo) return "La foto no puede pesar más de 2 MB.";
        var jpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var png = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        if (jpeg && contentType == "image/jpeg") return null;
        if (png && contentType == "image/png") return null;
        return "La foto debe ser una imagen JPEG o PNG válida.";
    }

    public static string Extension(string? contentType) => contentType == "image/png" ? ".png" : ".jpg";

    /// Nombre de archivo seguro para guardar la foto de una credencial.
    public static string NombreArchivo(int id, string? contentType) => $"foto-{id:D5}{Extension(contentType)}";

    public static string? Ruta(IWebHostEnvironment env, string? fotoArchivo)
    {
        if (string.IsNullOrWhiteSpace(fotoArchivo)) return null;
        var nombre = Path.GetFileName(fotoArchivo); // nunca rutas relativas: solo el nombre
        var ruta = Path.Combine(Directorio(env), nombre);
        return File.Exists(ruta) ? ruta : null;
    }
}

/// Almacén de credenciales. `List()` devuelve todas, de la más nueva a la más antigua.
public interface ICredencialStore
{
    IReadOnlyList<Credencial> List();
    Credencial? Find(int id);
    (int Status, string? Error, int Id) Crear(string usuario, string nombre, string puesto, string nss,
        string cedula, string vigencia, string fotoArchivo, string autor);
    (int Status, string? Error) Actualizar(int id, string nombre, string puesto, string nss, string cedula, string vigencia);
    /// Reemplaza el nombre del archivo de foto; devuelve el archivo anterior para que la API lo borre del disco.
    (int Status, string? Error, string? FotoAnterior) CambiarFoto(int id, string fotoArchivo);
    /// Baja la credencial; devuelve el archivo de foto para que la API lo borre del disco.
    (int Status, string? Error, string? FotoArchivo) Eliminar(int id);
}

// ============================================================================
//  Implementación en MySQL
// ============================================================================

public class MySqlCredencialStore(IConfiguration config) : ICredencialStore
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    /// Crea la tabla `credenciales` si no existe (primer arranque tras la actualización).
    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`credenciales` (
              `id`          INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `usuario`     VARCHAR(40)  NOT NULL,
              `nombre`      VARCHAR(120) NOT NULL,
              `puesto`      VARCHAR(80)  NOT NULL,
              `nss`         VARCHAR(20)  NOT NULL DEFAULT '',
              `cedula`      VARCHAR(20)  NOT NULL DEFAULT '',
              `vigencia`    VARCHAR(7)   NOT NULL,
              `fotoArchivo` VARCHAR(40)  NOT NULL DEFAULT '',
              `creadoPor`   VARCHAR(40)  NOT NULL DEFAULT '',
              `creado`      DATETIME(6)  NOT NULL,
              `actualizado` DATETIME(6)  NOT NULL,
              KEY `ix_credenciales_usuario` (`usuario`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    private static Credencial LeerFila(MySqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
        r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetDateTime(9), r.GetDateTime(10));

    private const string Select = """
        SELECT id, usuario, nombre, puesto, nss, cedula, vigencia, fotoArchivo, creadoPor, creado, actualizado
        FROM credenciales
        """;

    public IReadOnlyList<Credencial> List()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = Select + " ORDER BY creado DESC, id DESC";
        using var r = cmd.ExecuteReader();
        var lista = new List<Credencial>();
        while (r.Read()) lista.Add(LeerFila(r));
        return lista;
    }

    public Credencial? Find(int id)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = Select + " WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? LeerFila(r) : null;
    }

    public (int Status, string? Error, int Id) Crear(string usuario, string nombre, string puesto, string nss,
        string cedula, string vigencia, string fotoArchivo, string autor)
    {
        if (ReglasCredencial.Validar(usuario, nombre, puesto, nss, cedula, vigencia) is { } e) return (400, e, 0);
        var ahora = DateTime.UtcNow;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO credenciales (usuario, nombre, puesto, nss, cedula, vigencia, fotoArchivo, creadoPor, creado, actualizado)
            VALUES (@u, @n, @p, @ns, @ce, @v, @f, @por, @cre, @act);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
        cmd.Parameters.AddWithValue("@n", (nombre ?? "").Trim());
        cmd.Parameters.AddWithValue("@p", (puesto ?? "").Trim());
        cmd.Parameters.AddWithValue("@ns", (nss ?? "").Trim());
        cmd.Parameters.AddWithValue("@ce", (cedula ?? "").Trim());
        cmd.Parameters.AddWithValue("@v", (vigencia ?? "").Trim());
        cmd.Parameters.AddWithValue("@f", fotoArchivo ?? "");
        cmd.Parameters.AddWithValue("@por", autor ?? "");
        cmd.Parameters.AddWithValue("@cre", ahora);
        cmd.Parameters.AddWithValue("@act", ahora);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return (201, null, id);
    }

    public (int Status, string? Error) Actualizar(int id, string nombre, string puesto, string nss, string cedula, string vigencia)
    {
        if (ReglasCredencial.Validar("x", nombre, puesto, nss, cedula, vigencia) is { } e) return (400, e);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE credenciales SET nombre=@n, puesto=@p, nss=@ns, cedula=@ce, vigencia=@v, actualizado=@act
            WHERE id=@id
            """;
        cmd.Parameters.AddWithValue("@n", (nombre ?? "").Trim());
        cmd.Parameters.AddWithValue("@p", (puesto ?? "").Trim());
        cmd.Parameters.AddWithValue("@ns", (nss ?? "").Trim());
        cmd.Parameters.AddWithValue("@ce", (cedula ?? "").Trim());
        cmd.Parameters.AddWithValue("@v", (vigencia ?? "").Trim());
        cmd.Parameters.AddWithValue("@act", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0 ? (204, null) : (404, "La credencial no existe.");
    }

    public (int Status, string? Error, string? FotoAnterior) CambiarFoto(int id, string fotoArchivo)
    {
        var actual = Find(id);
        if (actual is null) return (404, "La credencial no existe.", null);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE credenciales SET fotoArchivo=@f, actualizado=@act WHERE id=@id";
        cmd.Parameters.AddWithValue("@f", fotoArchivo ?? "");
        cmd.Parameters.AddWithValue("@act", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        return (204, null, string.IsNullOrWhiteSpace(actual.FotoArchivo) ? null : actual.FotoArchivo);
    }

    public (int Status, string? Error, string? FotoArchivo) Eliminar(int id)
    {
        var actual = Find(id);
        if (actual is null) return (404, "La credencial no existe.", null);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM credenciales WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        return (204, null, string.IsNullOrWhiteSpace(actual.FotoArchivo) ? null : actual.FotoArchivo);
    }
}

// ============================================================================
//  Implementación en JSON (respaldo cuando MySQL no responde)
// ============================================================================

public class JsonCredencialStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonCredencialStore> logger) : ICredencialStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:CredencialFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "credenciales.json");

    private List<Credencial> Leer()
    {
        if (!File.Exists(_path)) return new List<Credencial>();
        try
        {
            return JsonSerializer.Deserialize<List<Credencial>>(File.ReadAllText(_path), Json) ?? new List<Credencial>();
        }
        catch (Exception e) { logger.LogWarning("No se pudo leer credenciales.json: {Error}", e.Message); return new List<Credencial>(); }
    }

    private void Escribir(List<Credencial> lista)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(lista, Json));
    }

    public IReadOnlyList<Credencial> List() { lock (_lock) return Leer().OrderByDescending(c => c.Creado).ThenByDescending(c => c.Id).ToList(); }

    public Credencial? Find(int id) { lock (_lock) return Leer().Find(c => c.Id == id); }

    public (int Status, string? Error, int Id) Crear(string usuario, string nombre, string puesto, string nss,
        string cedula, string vigencia, string fotoArchivo, string autor)
    {
        if (ReglasCredencial.Validar(usuario, nombre, puesto, nss, cedula, vigencia) is { } e) return (400, e, 0);
        lock (_lock)
        {
            var lista = Leer();
            var id = lista.Count == 0 ? 1 : lista.Max(c => c.Id) + 1;
            var ahora = DateTime.UtcNow;
            lista.Add(new Credencial(id, (usuario ?? "").Trim(), (nombre ?? "").Trim(), (puesto ?? "").Trim(),
                (nss ?? "").Trim(), (cedula ?? "").Trim(), (vigencia ?? "").Trim(), fotoArchivo ?? "",
                autor ?? "", ahora, ahora));
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar credenciales.json: {Error}", ex.Message); return (500, "No se pudo crear la credencial.", 0); }
            return (201, null, id);
        }
    }

    public (int Status, string? Error) Actualizar(int id, string nombre, string puesto, string nss, string cedula, string vigencia)
    {
        if (ReglasCredencial.Validar("x", nombre, puesto, nss, cedula, vigencia) is { } e) return (400, e);
        lock (_lock)
        {
            var lista = Leer();
            var i = lista.FindIndex(c => c.Id == id);
            if (i < 0) return (404, "La credencial no existe.");
            lista[i] = lista[i] with
            {
                Nombre = (nombre ?? "").Trim(),
                Puesto = (puesto ?? "").Trim(),
                Nss = (nss ?? "").Trim(),
                Cedula = (cedula ?? "").Trim(),
                Vigencia = (vigencia ?? "").Trim(),
                Actualizado = DateTime.UtcNow
            };
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar credenciales.json: {Error}", ex.Message); return (500, "No se pudo guardar la credencial."); }
            return (204, null);
        }
    }

    public (int Status, string? Error, string? FotoAnterior) CambiarFoto(int id, string fotoArchivo)
    {
        lock (_lock)
        {
            var lista = Leer();
            var i = lista.FindIndex(c => c.Id == id);
            if (i < 0) return (404, "La credencial no existe.", null);
            var anterior = string.IsNullOrWhiteSpace(lista[i].FotoArchivo) ? null : lista[i].FotoArchivo;
            lista[i] = lista[i] with { FotoArchivo = fotoArchivo ?? "", Actualizado = DateTime.UtcNow };
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar credenciales.json: {Error}", ex.Message); return (500, "No se pudo guardar la foto.", null); }
            return (204, null, anterior);
        }
    }

    public (int Status, string? Error, string? FotoArchivo) Eliminar(int id)
    {
        lock (_lock)
        {
            var lista = Leer();
            var i = lista.FindIndex(c => c.Id == id);
            if (i < 0) return (404, "La credencial no existe.", null);
            var foto = string.IsNullOrWhiteSpace(lista[i].FotoArchivo) ? null : lista[i].FotoArchivo;
            lista.RemoveAt(i);
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar credenciales.json: {Error}", ex.Message); return (500, "No se pudo eliminar la credencial.", null); }
            return (204, null, foto);
        }
    }
}
