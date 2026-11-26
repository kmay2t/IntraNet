using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Un acta administrativa: el documento con el que Recursos Humanos / administrativos deja
/// constancia de un hecho (falta, incidente, acuerdo) relacionado con una persona. `Usuario`
/// liga el acta a la cuenta de la intranet cuando la persona tiene cuenta (puede quedar vacía
/// si se levanta el acta a alguien sin cuenta); `Nombre` y `Puesto` son los datos que se
/// imprimen. El folio se genera solo: ACTA-<año>-<consecutivo>.
/// `Tipo` define la plantilla con la que se imprime: `conducta` (mala conducta, formato general)
/// o `faltas` (faltas injustificadas). `Hora` es texto libre para el preámbulo ("10:26").
public record Acta(
    int Id,
    string Folio,
    string Tipo,
    string Usuario,
    string Nombre,
    string Puesto,
    string Lugar,
    DateTime FechaHechos,
    string Hora,
    string Hechos,
    string Acuerdos,
    string Testigos,
    string CreadoPor,
    DateTime Creado,
    DateTime Actualizado);

/// Reglas de los campos de un acta (compartidas por la API y los almacenes).
public static class ReglasActa
{
    public const int NombreMin = 3;
    public const int NombreMax = 120;
    public const int PuestoMax = 80;
    public const int LugarMax = 120;
    public const int HoraMax = 20;
    public const int HechosMin = 10;
    public const int HechosMax = 4000;
    public const int AcuerdosMax = 2000;
    public const int TestigosMax = 300;

    /// Tipos de acta admitidos (plantilla de impresión).
    public const string TipoConducta = "conducta";
    public const string TipoFaltas = "faltas";
    public static readonly string[] Tipos = { TipoConducta, TipoFaltas };

    /// Devuelve el tipo normalizado; si viene vacío o no es válido, cae en `conducta`.
    public static string NormalizarTipo(string? tipo)
    {
        var t = (tipo ?? "").Trim().ToLowerInvariant();
        return Array.IndexOf(Tipos, t) >= 0 ? t : TipoConducta;
    }

    /// Mensaje de error o null si los campos son aceptables.
    public static string? Validar(string? nombre, string? puesto, string? lugar, string? hechos, string? acuerdos, string? testigos, string? hora)
    {
        var n = (nombre ?? "").Trim();
        if (n.Length < NombreMin || n.Length > NombreMax)
            return $"El nombre de la persona debe tener entre {NombreMin} y {NombreMax} caracteres.";
        if ((puesto ?? "").Trim().Length > PuestoMax)
            return $"El puesto no puede pasar de {PuestoMax} caracteres.";
        if ((lugar ?? "").Trim().Length > LugarMax)
            return $"El lugar no puede pasar de {LugarMax} caracteres.";
        if ((hora ?? "").Trim().Length > HoraMax)
            return $"La hora no puede pasar de {HoraMax} caracteres.";
        var h = (hechos ?? "").Trim();
        if (h.Length < HechosMin || h.Length > HechosMax)
            return $"La relación de hechos debe tener entre {HechosMin} y {HechosMax} caracteres.";
        if ((acuerdos ?? "").Trim().Length > AcuerdosMax)
            return $"Los acuerdos no pueden pasar de {AcuerdosMax} caracteres.";
        if ((testigos ?? "").Trim().Length > TestigosMax)
            return $"Los testigos no pueden pasar de {TestigosMax} caracteres.";
        return null;
    }

    /// Folio siguiente al mayor consecutivo del año: ACTA-2026-0001, ACTA-2026-0002…
    public static string SiguienteFolio(IEnumerable<string> folios, DateTime ahora)
    {
        var prefijo = $"ACTA-{ahora:yyyy}-";
        var max = folios
            .Where(f => f.StartsWith(prefijo, StringComparison.Ordinal)
                        && int.TryParse(f[prefijo.Length..], out _))
            .Select(f => int.Parse(f[prefijo.Length..]))
            .DefaultIfEmpty(0).Max();
        return $"{prefijo}{max + 1:D4}";
    }
}

/// Almacén de actas administrativas. `List()` devuelve todas, de la más nueva a la más antigua.
public interface IActaStore
{
    IReadOnlyList<Acta> List();
    (int Status, string? Error, Acta? Acta) Crear(string usuario, string nombre, string puesto, string lugar,
        DateTime fechaHechos, string hechos, string acuerdos, string testigos, string autor, string tipo, string hora);
    (int Status, string? Error) Actualizar(int id, string usuario, string nombre, string puesto, string lugar,
        DateTime fechaHechos, string hechos, string acuerdos, string testigos, string tipo, string hora);
    (int Status, string? Error) Eliminar(int id);
}

// ============================================================================
//  Implementación en MySQL
// ============================================================================

public class MySqlActaStore(IConfiguration config) : IActaStore
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    /// Crea la tabla `actas` si no existe (primer arranque tras la actualización).
    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`actas` (
              `id`          INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `folio`       VARCHAR(20)   NOT NULL,
              `tipo`        VARCHAR(20)   NOT NULL DEFAULT 'conducta',
              `usuario`     VARCHAR(40)   NOT NULL DEFAULT '',
              `nombre`      VARCHAR(120)  NOT NULL,
              `puesto`      VARCHAR(80)   NOT NULL DEFAULT '',
              `lugar`       VARCHAR(120)  NOT NULL DEFAULT '',
              `fechaHechos` DATETIME(6)   NOT NULL,
              `hora`        VARCHAR(20)   NOT NULL DEFAULT '',
              `hechos`      VARCHAR(4000) NOT NULL,
              `acuerdos`    VARCHAR(2000) NOT NULL DEFAULT '',
              `testigos`    VARCHAR(300)  NOT NULL DEFAULT '',
              `creadoPor`   VARCHAR(40)   NOT NULL DEFAULT '',
              `creado`      DATETIME(6)   NOT NULL,
              `actualizado` DATETIME(6)   NOT NULL,
              UNIQUE KEY `uq_actas_folio` (`folio`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();

        // Tabla creada en una versión previa (sin tipo/hora): agregar solo las columnas que falten.
        foreach (var (columna, ddl) in new[]
        {
            ("tipo", "ADD COLUMN `tipo` VARCHAR(20) NOT NULL DEFAULT 'conducta' AFTER `folio`"),
            ("hora", "ADD COLUMN `hora` VARCHAR(20) NOT NULL DEFAULT '' AFTER `fechaHechos`"),
        })
        {
            if (ColumnaExiste(cn, db, "actas", columna)) continue;
            using var alter = cn.CreateCommand();
            alter.CommandText = $"ALTER TABLE `{db}`.`actas` {ddl}";
            alter.ExecuteNonQuery();
        }
    }

    private static bool ColumnaExiste(MySqlConnection cn, string db, string tabla, string columna)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @t AND COLUMN_NAME = @c
            """;
        cmd.Parameters.AddWithValue("@db", db);
        cmd.Parameters.AddWithValue("@t", tabla);
        cmd.Parameters.AddWithValue("@c", columna);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public IReadOnlyList<Acta> List()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT id, folio, tipo, usuario, nombre, puesto, lugar, fechaHechos, hora, hechos, acuerdos, testigos,
                   creadoPor, creado, actualizado
            FROM actas ORDER BY creado DESC, id DESC
            """;
        using var r = cmd.ExecuteReader();
        var lista = new List<Acta>();
        while (r.Read())
            lista.Add(new Acta(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetString(5), r.GetString(6), r.GetDateTime(7), r.GetString(8), r.GetString(9),
                r.GetString(10), r.GetString(11), r.GetString(12), r.GetDateTime(13), r.GetDateTime(14)));
        return lista;
    }

    public (int Status, string? Error, Acta? Acta) Crear(string usuario, string nombre, string puesto, string lugar,
        DateTime fechaHechos, string hechos, string acuerdos, string testigos, string autor, string tipo, string hora)
    {
        if (ReglasActa.Validar(nombre, puesto, lugar, hechos, acuerdos, testigos, hora) is { } e) return (400, e, null);
        tipo = ReglasActa.NormalizarTipo(tipo);
        var ahora = DateTime.UtcNow;
        var folio = ReglasActa.SiguienteFolio(List().Select(a => a.Folio), ahora);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO actas (folio, tipo, usuario, nombre, puesto, lugar, fechaHechos, hora, hechos, acuerdos, testigos,
                               creadoPor, creado, actualizado)
            VALUES (@fo, @ti, @u, @n, @p, @l, @fh, @ho, @h, @ac, @te, @por, @cre, @act);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@fo", folio);
        cmd.Parameters.AddWithValue("@ti", tipo);
        cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
        cmd.Parameters.AddWithValue("@n", (nombre ?? "").Trim());
        cmd.Parameters.AddWithValue("@p", (puesto ?? "").Trim());
        cmd.Parameters.AddWithValue("@l", (lugar ?? "").Trim());
        cmd.Parameters.AddWithValue("@fh", fechaHechos);
        cmd.Parameters.AddWithValue("@ho", (hora ?? "").Trim());
        cmd.Parameters.AddWithValue("@h", (hechos ?? "").Trim());
        cmd.Parameters.AddWithValue("@ac", (acuerdos ?? "").Trim());
        cmd.Parameters.AddWithValue("@te", (testigos ?? "").Trim());
        cmd.Parameters.AddWithValue("@por", autor ?? "");
        cmd.Parameters.AddWithValue("@cre", ahora);
        cmd.Parameters.AddWithValue("@act", ahora);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return (201, null, new Acta(id, folio, tipo, (usuario ?? "").Trim(), (nombre ?? "").Trim(), (puesto ?? "").Trim(),
            (lugar ?? "").Trim(), fechaHechos, (hora ?? "").Trim(), (hechos ?? "").Trim(), (acuerdos ?? "").Trim(),
            (testigos ?? "").Trim(), autor ?? "", ahora, ahora));
    }

    public (int Status, string? Error) Actualizar(int id, string usuario, string nombre, string puesto, string lugar,
        DateTime fechaHechos, string hechos, string acuerdos, string testigos, string tipo, string hora)
    {
        if (ReglasActa.Validar(nombre, puesto, lugar, hechos, acuerdos, testigos, hora) is { } e) return (400, e);
        tipo = ReglasActa.NormalizarTipo(tipo);
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            UPDATE actas SET tipo=@ti, usuario=@u, nombre=@n, puesto=@p, lugar=@l, fechaHechos=@fh, hora=@ho,
                   hechos=@h, acuerdos=@ac, testigos=@te, actualizado=@act WHERE id=@id
            """;
        cmd.Parameters.AddWithValue("@ti", tipo);
        cmd.Parameters.AddWithValue("@u", (usuario ?? "").Trim());
        cmd.Parameters.AddWithValue("@n", (nombre ?? "").Trim());
        cmd.Parameters.AddWithValue("@p", (puesto ?? "").Trim());
        cmd.Parameters.AddWithValue("@l", (lugar ?? "").Trim());
        cmd.Parameters.AddWithValue("@fh", fechaHechos);
        cmd.Parameters.AddWithValue("@ho", (hora ?? "").Trim());
        cmd.Parameters.AddWithValue("@h", (hechos ?? "").Trim());
        cmd.Parameters.AddWithValue("@ac", (acuerdos ?? "").Trim());
        cmd.Parameters.AddWithValue("@te", (testigos ?? "").Trim());
        cmd.Parameters.AddWithValue("@act", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0 ? (204, null) : (404, "El acta no existe.");
    }

    public (int Status, string? Error) Eliminar(int id)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM actas WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0 ? (204, null) : (404, "El acta no existe.");
    }
}

// ============================================================================
//  Implementación en JSON (respaldo cuando MySQL no responde)
// ============================================================================

public class JsonActaStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonActaStore> logger) : IActaStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:ActaFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "actas.json");

    private List<Acta> Leer()
    {
        if (!File.Exists(_path)) return new List<Acta>();
        try
        {
            return JsonSerializer.Deserialize<List<Acta>>(File.ReadAllText(_path), Json) ?? new List<Acta>();
        }
        catch (Exception e) { logger.LogWarning("No se pudo leer actas.json: {Error}", e.Message); return new List<Acta>(); }
    }

    private void Escribir(List<Acta> lista)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(lista, Json));
    }

    public IReadOnlyList<Acta> List() { lock (_lock) return Leer().OrderByDescending(a => a.Creado).ThenByDescending(a => a.Id).ToList(); }

    public (int Status, string? Error, Acta? Acta) Crear(string usuario, string nombre, string puesto, string lugar,
        DateTime fechaHechos, string hechos, string acuerdos, string testigos, string autor, string tipo, string hora)
    {
        if (ReglasActa.Validar(nombre, puesto, lugar, hechos, acuerdos, testigos, hora) is { } e) return (400, e, null);
        tipo = ReglasActa.NormalizarTipo(tipo);
        lock (_lock)
        {
            var lista = Leer();
            var ahora = DateTime.UtcNow;
            var folio = ReglasActa.SiguienteFolio(lista.Select(a => a.Folio), ahora);
            var id = lista.Count == 0 ? 1 : lista.Max(a => a.Id) + 1;
            var acta = new Acta(id, folio, tipo, (usuario ?? "").Trim(), (nombre ?? "").Trim(), (puesto ?? "").Trim(),
                (lugar ?? "").Trim(), fechaHechos, (hora ?? "").Trim(), (hechos ?? "").Trim(), (acuerdos ?? "").Trim(),
                (testigos ?? "").Trim(), autor ?? "", ahora, ahora);
            lista.Add(acta);
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar actas.json: {Error}", ex.Message); return (500, "No se pudo crear el acta.", null); }
            return (201, null, acta);
        }
    }

    public (int Status, string? Error) Actualizar(int id, string usuario, string nombre, string puesto, string lugar,
        DateTime fechaHechos, string hechos, string acuerdos, string testigos, string tipo, string hora)
    {
        if (ReglasActa.Validar(nombre, puesto, lugar, hechos, acuerdos, testigos, hora) is { } e) return (400, e);
        lock (_lock)
        {
            var lista = Leer();
            var i = lista.FindIndex(a => a.Id == id);
            if (i < 0) return (404, "El acta no existe.");
            lista[i] = lista[i] with
            {
                Tipo = ReglasActa.NormalizarTipo(tipo),
                Usuario = (usuario ?? "").Trim(),
                Nombre = (nombre ?? "").Trim(),
                Puesto = (puesto ?? "").Trim(),
                Lugar = (lugar ?? "").Trim(),
                FechaHechos = fechaHechos,
                Hora = (hora ?? "").Trim(),
                Hechos = (hechos ?? "").Trim(),
                Acuerdos = (acuerdos ?? "").Trim(),
                Testigos = (testigos ?? "").Trim(),
                Actualizado = DateTime.UtcNow
            };
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar actas.json: {Error}", ex.Message); return (500, "No se pudo guardar el acta."); }
            return (204, null);
        }
    }

    public (int Status, string? Error) Eliminar(int id)
    {
        lock (_lock)
        {
            var lista = Leer();
            var antes = lista.Count;
            lista.RemoveAll(a => a.Id == id);
            if (lista.Count == antes) return (404, "El acta no existe.");
            try { Escribir(lista); } catch (Exception ex) { logger.LogWarning("No se pudo guardar actas.json: {Error}", ex.Message); return (500, "No se pudo eliminar el acta."); }
            return (204, null);
        }
    }
}
