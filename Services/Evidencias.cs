using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Almacén de las evidencias (foto o archivo) que respaldan el trabajo al completar un elemento del
/// calendario. Solo se agrega: la evidencia es parte del cierre y no se reescribe ni se borra, igual
/// que el historial. Las reglas y los permisos están en `EvidenciaService`.
public interface IEvidenciaStore
{
    IReadOnlyList<Evidencia> PorItem(long actividadId);
    Evidencia? Find(long id);
    /// <returns>201 creado (con Id), 400 datos inválidos.</returns>
    (int Status, string? Error, Evidencia? Item) Crear(Evidencia borrador);
    /// <returns>204 eliminado, 404 no existía. Se usa solo para deshacer el adjunto cuando el cierre falla.</returns>
    (int Status, string? Error) Borrar(long id);
}

public abstract class EvidenciaStoreBase : IEvidenciaStore
{
    public abstract IReadOnlyList<Evidencia> PorItem(long actividadId);
    public abstract Evidencia? Find(long id);
    public abstract (int, string?, Evidencia?) Crear(Evidencia borrador);
    public abstract (int, string?) Borrar(long id);

    protected static string? Validar(Evidencia e) =>
        e.ActividadId <= 0 ? "La evidencia debe pertenecer a un elemento del calendario."
        : EvidenciaRules.ExtensionPermitida(e.NombreArchivo) ? null
        : $"«{e.NombreArchivo}» no es una foto ni un documento aceptado.";

    /// Fija el nombre interno y el tipo MIME a partir de la extensión (nunca del contenido que
    /// envió el navegador) y recorta el nombre visible a lo que cabe en la columna.
    protected static Evidencia Limpiar(Evidencia e) => e with
    {
        NombreArchivo = EvidenciaRules.SoloNombre(e.NombreArchivo),
        Tipo = EvidenciaRules.TipoDe(e.NombreArchivo),
        Ruta = string.IsNullOrWhiteSpace(e.Ruta) ? $"{Guid.NewGuid():N}{EvidenciaRules.Extension(e.NombreArchivo)}" : e.Ruta,
        SubidoPor = (e.SubidoPor ?? "").Trim(),
    };

    protected static string Recortar(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }
}

/// Respaldo en archivo JSON (App_Data/evidencias.json), cuando MySQL no responde.
public class JsonEvidenciaStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonEvidenciaStore> logger)
    : EvidenciaStoreBase
{
    private sealed class Archivo
    {
        public long NextId { get; set; } = 1;
        public List<Evidencia> Items { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:EvidenciasFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "evidencias.json");
    private Archivo _data = new();
    private bool _cargado = false;

    private Archivo Cargar()
    {
        if (!File.Exists(_path)) return new Archivo();
        try { return JsonSerializer.Deserialize<Archivo>(File.ReadAllText(_path), Json) ?? new Archivo(); }
        catch (Exception e) { logger.LogWarning("No se pudo leer evidencias.json: {Error}", e.Message); return new Archivo(); }
    }

    private void GuardarArchivo()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_data, Json));
    }

    private Archivo Datos()
    {
        if (!_cargado) { _data = Cargar(); _cargado = true; }
        return _data;
    }

    public override IReadOnlyList<Evidencia> PorItem(long actividadId)
    {
        lock (_lock) { return Datos().Items.Where(e => e.ActividadId == actividadId).OrderBy(e => e.Subido).ThenBy(e => e.Id).ToList(); }
    }

    public override Evidencia? Find(long id)
    {
        lock (_lock) { return Datos().Items.FirstOrDefault(e => e.Id == id); }
    }

    public override (int, string?, Evidencia?) Crear(Evidencia borrador)
    {
        var limpio = Limpiar(borrador);
        if (Validar(limpio) is string error) return (400, error, null);
        lock (_lock)
        {
            var data = Datos();
            var item = limpio with { Id = data.NextId++ };
            data.Items.Add(item);
            try { GuardarArchivo(); }
            catch (Exception e) { logger.LogWarning("No se pudo escribir evidencias.json: {Error}", e.Message); return (500, "No se pudo guardar la evidencia.", null); }
            return (201, null, item);
        }
    }

    public override (int, string?) Borrar(long id)
    {
        lock (_lock)
        {
            var data = Datos();
            if (data.Items.RemoveAll(e => e.Id == id) == 0) return (404, "La evidencia no existe.");
            try { GuardarArchivo(); }
            catch (Exception e) { logger.LogWarning("No se pudo escribir evidencias.json: {Error}", e.Message); return (500, "No se pudo borrar la evidencia."); }
            return (204, null);
        }
    }
}

/// Evidencias en MySQL (misma base de usuarios): tabla `evidencias`.
public class MySqlEvidenciaStore(IConfiguration config, ILogger<MySqlEvidenciaStore> logger) : EvidenciaStoreBase
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    private const string Columnas =
        "id, actividad_id, nombre_archivo, ruta, tipo, tamano, subido_por, subido";

    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`evidencias` (
              `id`             BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `actividad_id`   BIGINT       NOT NULL,
              `nombre_archivo` VARCHAR(200) NOT NULL,
              `ruta`           VARCHAR(300) NOT NULL,
              `tipo`           VARCHAR(120) NOT NULL DEFAULT 'application/octet-stream',
              `tamano`         BIGINT       NOT NULL DEFAULT 0,
              `subido_por`     VARCHAR(40)  NOT NULL DEFAULT '',
              `subido`         DATETIME(6)  NOT NULL,
              KEY `idx_evidencias_actividad` (`actividad_id`, `subido`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public override IReadOnlyList<Evidencia> PorItem(long actividadId)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"SELECT {Columnas} FROM evidencias WHERE actividad_id = @i ORDER BY subido, id";
            cmd.Parameters.AddWithValue("@i", actividadId);
            var lista = new List<Evidencia>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) lista.Add(Leer(r));
            return lista;
        }
        catch (Exception e)
        {
            logger.LogWarning("No se pudo leer la evidencia: {Error}", e.Message);
            return Array.Empty<Evidencia>();
        }
    }

    public override Evidencia? Find(long id)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"SELECT {Columnas} FROM evidencias WHERE id = @i";
            cmd.Parameters.AddWithValue("@i", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? Leer(r) : null;
        }
        catch (Exception e)
        {
            logger.LogWarning("No se pudo leer la evidencia {Id}: {Error}", id, e.Message);
            return null;
        }
    }

    public override (int, string?, Evidencia?) Crear(Evidencia borrador)
    {
        var limpio = Limpiar(borrador);
        if (Validar(limpio) is string error) return (400, error, null);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO evidencias (actividad_id, nombre_archivo, ruta, tipo, tamano, subido_por, subido)
                VALUES (@a, @n, @r, @t, @s, @p, @u);
                SELECT LAST_INSERT_ID();
                """;
            cmd.Parameters.AddWithValue("@a", limpio.ActividadId);
            cmd.Parameters.AddWithValue("@n", Recortar(limpio.NombreArchivo, 200));
            cmd.Parameters.AddWithValue("@r", Recortar(limpio.Ruta, 300));
            cmd.Parameters.AddWithValue("@t", Recortar(limpio.Tipo, 120));
            cmd.Parameters.AddWithValue("@s", limpio.Tamano);
            cmd.Parameters.AddWithValue("@p", Recortar(limpio.SubidoPor, 40));
            cmd.Parameters.AddWithValue("@u", limpio.Subido);
            var id = Convert.ToInt64(cmd.ExecuteScalar());
            return (201, null, id == 0 ? null : limpio with { Id = id });
        }
        catch (Exception e)
        {
            logger.LogWarning("No se pudo guardar la evidencia: {Error}", e.Message);
            return (500, "No se pudo guardar la evidencia en la base de datos.", null);
        }
    }

    public override (int, string?) Borrar(long id)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM evidencias WHERE id = @i";
            cmd.Parameters.AddWithValue("@i", id);
            return cmd.ExecuteNonQuery() > 0 ? (204, null) : (404, "La evidencia no existe.");
        }
        catch (Exception e)
        {
            logger.LogWarning("No se pudo borrar la evidencia {Id}: {Error}", id, e.Message);
            return (500, "No se pudo borrar la evidencia.");
        }
    }

    private static Evidencia Leer(MySqlDataReader r) => new(
        r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4),
        r.GetInt64(5), r.GetString(6), DateTime.SpecifyKind(r.GetDateTime(7), DateTimeKind.Unspecified));
}

/// Reglas de negocio de la evidencia: quién puede adjuntar, qué se acepta y dónde vive el archivo.
/// El contenido va a disco con nombre GUID (nunca con el nombre que envía el navegador) dentro de
/// `Gestion:EvidenciasDir`, por defecto `App_Data/evidencias`; la tabla guarda la ruta y los metadatos.
public class EvidenciaService(
    IEvidenciaStore store,
    IActivityStore items,
    IHistorialStore historial,
    IAuditLog bitacora,
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<EvidenciaService> logger)
{
    public string Carpeta { get; } = string.IsNullOrWhiteSpace(config["Gestion:EvidenciasDir"])
        ? Path.Combine(env.ContentRootPath, "App_Data", "evidencias")
        : config["Gestion:EvidenciasDir"]!;

    public void AsegurarCarpeta()
    {
        try { Directory.CreateDirectory(Carpeta); }
        catch (Exception e) { logger.LogWarning("No se pudo crear la carpeta de evidencias ({Carpeta}): {Error}", Carpeta, e.Message); }
    }

    /// Adjunta la evidencia a un elemento abierto. Solo puede hacerlo quien puede completarlo: el
    /// responsable (o quien ve todo) con el permiso de actualizar estatus. Deja el adjunto en el
    /// historial de cambios y en la bitácora.
    public (int Status, string? Error, IReadOnlyList<Evidencia>?) Adjuntar(
        GestionService.Sesion s, long actividadId, IReadOnlyList<(string Nombre, byte[]? Contenido)> archivos, string? ip)
    {
        var actual = items.Find(actividadId);
        if (actual is null) return (404, "La actividad no existe.", null);
        if (!GestionService.EsDuenoOAdmin(s, actual)) return (403, "No puedes adjuntar evidencia a un elemento que no tienes asignado.", null);
        if (!s.Puede(CatalogoPermisos.ActualizarEstatus)) return (403, "Tu rol no puede actualizar estatus, así que tampoco adjuntar su evidencia.", null);
        if (!EstadosActividad.Abierto(actual.Estado)) return (400, "El elemento ya está completado o cancelado; la evidencia se adjunta al completarlo.", null);
        if (archivos.Count == 0) return (400, "Adjunta la evidencia: una foto o un archivo que respalde el trabajo.", null);
        if (archivos.Count > EvidenciaRules.MaxArchivosPorEnvio)
            return (400, $"Se admiten hasta {EvidenciaRules.MaxArchivosPorEnvio} archivos por envío.", null);

        // Se valida todo antes de escribir: un envío con un archivo malo no deja media evidencia.
        foreach (var (nombre, contenido) in archivos)
            if (EvidenciaRules.ArchivoError(nombre, contenido) is string eA) return (400, eA, null);

        AsegurarCarpeta();
        var guardadas = new List<Evidencia>();
        foreach (var (nombre, contenido) in archivos)
        {
            var limpio = EvidenciaRules.SoloNombre(nombre);
            var ruta = $"{Guid.NewGuid():N}{EvidenciaRules.Extension(limpio)}";
            try
            {
                File.WriteAllBytes(Path.Combine(Carpeta, ruta), contenido!);
            }
            catch (Exception e)
            {
                logger.LogError("No se pudo escribir la evidencia {Ruta}: {Error}", ruta, e.Message);
                foreach (var g in guardadas) BorrarArchivo(g.Ruta);
                return (500, "No se pudo guardar la evidencia en disco.", null);
            }
            var (status, error, creada) = store.Crear(new Evidencia(0, actividadId, limpio, ruta,
                EvidenciaRules.TipoDe(limpio), contenido!.LongLength, s.Usuario, DateTime.Now));
            if (status != 201 || creada is null)
            {
                BorrarArchivo(ruta);
                foreach (var g in guardadas) { BorrarArchivo(g.Ruta); store.Borrar(g.Id); }
                return (status, error ?? "No se pudo registrar la evidencia.", null);
            }
            guardadas.Add(creada);
        }

        var nombres = string.Join(", ", guardadas.Select(g => g.NombreArchivo));
        historial.Registrar(actividadId, s.Usuario, "Evidencia", "", $"{guardadas.Count} archivo(s)", nombres);
        bitacora.Register(s.Usuario, AuditEvents.AdjuntarEvidencia,
            $"{actual.Etiqueta} · {guardadas.Count} archivo(s) · {Recortar(nombres, 120)}", ip);
        return (201, null, guardadas);
    }

    /// La lista que muestra la tarjeta: la ve quien puede ver el elemento.
    public IReadOnlyList<Evidencia> Listar(GestionService.Sesion s, long actividadId)
    {
        var actual = items.Find(actividadId);
        if (actual is null || !GestionService.EsDuenoOAdmin(s, actual)) return Array.Empty<Evidencia>();
        return store.PorItem(actividadId);
    }

    /// Resuelve el archivo para servirlo, otra vez solo si quien pide puede ver el elemento.
    /// Devuelve `null` cuando la evidencia no existe o su archivo ya no está en disco.
    public (Evidencia Item, string Ruta)? ParaAbrir(GestionService.Sesion s, long id)
    {
        var e = store.Find(id);
        if (e is null) return null;
        var actual = items.Find(e.ActividadId);
        if (actual is null || !GestionService.EsDuenoOAdmin(s, actual)) return null;
        var absoluta = RutaAbsoluta(e.Ruta);
        return absoluta is null || !File.Exists(absoluta) ? null : (e, absoluta);
    }

    /// Quita el registro y su archivo (se usa para deshacer un cierre que no se completó).
    public void Descartar(Evidencia e)
    {
        store.Borrar(e.Id);
        BorrarArchivo(e.Ruta);
    }

    private string? RutaAbsoluta(string? relativa)
    {
        if (string.IsNullOrWhiteSpace(relativa)) return null;
        try
        {
            var raiz = Path.GetFullPath(Carpeta);
            var completa = Path.GetFullPath(Path.Combine(raiz, relativa));
            return completa.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? completa : null;
        }
        catch { return null; }
    }

    private void BorrarArchivo(string ruta)
    {
        try
        {
            var absoluta = RutaAbsoluta(ruta);
            if (absoluta is not null && File.Exists(absoluta)) File.Delete(absoluta);
        }
        catch (Exception e) { logger.LogWarning("No se pudo borrar la evidencia {Ruta}: {Error}", ruta, e.Message); }
    }

    private static string Recortar(string? s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }
}
