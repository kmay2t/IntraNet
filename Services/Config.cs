using System.Text.Json;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Configuración general del módulo Gestión (clave/valor): la periodicidad de reportes por defecto
/// y el interruptor de fecha límite. La página /admin/config/ expone las claves editables.
public interface IConfigStore
{
    string Get(string clave, string predeterminado);
    /// <returns>204 guardada.</returns>
    (int Status, string? Error) Set(string clave, string valor);
    IReadOnlyDictionary<string, string> Todo();
}

/// Claves conocidas y sus valores por defecto.
public static class ConfigKeys
{
    /// Cada cuántos días debe entregar reporte de estatus un ingeniero, por defecto,
    /// cuando el Admin no indica periodicidad al asignar.
    public const string PeriodicidadReporteDias = "reportes.periodicidadDias";

    /// Interruptor exclusivo del Administrador: con "false" la fecha límite no se pide, no se
    /// guarda, no se valida y no marca elementos como vencidos.
    public const string FechaLimiteHabilitada = "items.fechaLimiteHabilitada";

    /// Versión del catálogo de permisos contra la que se guardó la matriz (uso interno: permite
    /// completar las matrices guardadas cuando el catálogo agrega claves nuevas).
    public const string PermisosVersion = "permisos.versionCatalogo";

    // ---- catálogos que edita la pantalla Programador (se guardan como JSON) ----
    // Arreglo de textos: las actividades que se eligen al crear un elemento del calendario.
    public const string ActividadesLista = "gestion.actividades";
    // Objeto `actividad → { etiqueta, opciones }`: las subopciones de una actividad concreta
    // (los aplicativos de una Capacitación). La `etiqueta` es el nombre con que se muestra la lista.
    public const string ActividadesSubopciones = "gestion.subopciones";
    // Objeto `aplicativo → arreglo de tipos`: el tercer escalón de la cadena que se configura en
    // Programador (actividad → aplicativo → **tipo de aplicativo**). Vacío por defecto: mientras no se
    // escriban tipos para el aplicativo elegido, el diálogo + Nuevo no muestra ese campo, igual que
    // pasa con Región y con Servicio. Lo elegido se guarda dentro del título, no en una columna nueva.
    public const string SubopcionesTipos = "gestion.subopcionesTipos";
    // Arreglo de textos: los servicios (anatomía patológica, radiología…) que se pueden capturar.
    // Se conserva como lista general: es el respaldo de los hospitales que no tengan su propia lista
    // en `gestion.serviciosPorHospital` y la unión que se escribe al guardar por hospital.
    public const string ServiciosLista = "gestion.servicios";
    // Objeto `hospital → arreglo de servicios`: los servicios que ofrece cada hospital. Vacío por
    // defecto, y mientras esté vacío los formularios usan la lista general para todos.
    public const string ServiciosPorHospital = "gestion.serviciosPorHospital";
    // Arreglo de textos: los hospitales que aparecen en los formularios. Los cuatro de siempre
    // (HES/HGR/HGO/HI) siguen siendo válidos aunque se quiten de la lista, para no romper datos.
    public const string HospitalesLista = "gestion.hospitales";
    // Arreglo de textos: las regiones médicas (p. ej. «CMN La Raza»). Vacío por defecto: mientras
    // no se configure ninguna, los formularios no muestran el campo Región.
    public const string RegionesLista = "gestion.regiones";
    // Objeto `región → arreglo de hospitales`: el desglose de cada región. Un hospital pertenece a
    // una sola región; el servidor rechaza repetirlo. Lo que no esté aquí queda «Sin región».
    public const string HospitalesPorRegion = "gestion.hospitalesPorRegion";
    // Objeto `usuario → { region, todas }`: la región que ve cada usuario. Se edita desde la pantalla
    // **Usuarios** (no desde Programador) y vive aquí, y no en la tabla de usuarios, para no necesitar
    // una columna nueva: es el mismo almacén que ya guarda las regiones, así que sobrevive a apagar o
    // reiniciar el servidor. `todas: true` es la opción de perfil «Ver todas las regiones»; root la
    // tiene siempre, esté o no en esta lista. Un usuario que no aparece aquí no tiene región y ve todo,
    // para que nadie se quede sin pantalla al publicar este cambio.
    public const string RegionesPorUsuario = "gestion.regionesPorUsuario";

    /// Claves que se editan desde la página de Configuración (las demás son internas).
    public static readonly IReadOnlyList<string> Editables = new[] { PeriodicidadReporteDias, FechaLimiteHabilitada };

    /// Claves que se editan desde la pantalla Programador.
    public static readonly IReadOnlyList<string> DeCatalogo = new[]
    { ActividadesLista, ActividadesSubopciones, SubopcionesTipos, ServiciosLista, ServiciosPorHospital, HospitalesLista, RegionesLista, HospitalesPorRegion };

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [PeriodicidadReporteDias] = "7",
        [FechaLimiteHabilitada] = "true",
        [PermisosVersion] = "1",
        [ActividadesLista] = """
            ["Fusiones","Exportación de estudios DICOM","Revisión de migración","Capacitación",
             "Documentación","Verificación de servicio","Mantenimiento preventivo","Mantenimiento correctivo"]
            """,
        [ActividadesSubopciones] = """
            {"Capacitación":{"etiqueta":"Aplicativo","opciones":["Aplicativos Fuji","Aplicativos propios"]}}
            """,
        // Vacío a propósito: nadie conoce todavía los tipos de cada aplicativo. Mientras un aplicativo
        // no tenga lista propia, el campo «Tipo de aplicativo» no aparece en el formulario.
        [SubopcionesTipos] = "{}",
        [ServiciosLista] = """
            ["Anatomía Patológica","Radiología","Urgencias","Consulta Externa","Cirugía","Tococirugía"]
            """,
        // Vacío a propósito: mientras no se configure por hospital, los formularios usan la lista
        // general de arriba para todos (así sigue funcionando lo que ya estaba guardado).
        [ServiciosPorHospital] = "{}",
        [HospitalesLista] = """["HES","HGR","HGO","HI"]""",
        // Vacío a propósito: sin regiones configuradas el diálogo + Nuevo se ve exactamente igual
        // que antes (sin el campo Región).
        [RegionesLista] = "[]",
        [HospitalesPorRegion] = "{}",
        // Vacío a propósito: mientras no se le asigne región a nadie, todos ven todas las regiones,
        // igual que hasta ahora.
        [RegionesPorUsuario] = "{}",
    };

    public static bool EsClaveConocida(string clave) => Defaults.ContainsKey(clave);
}

public class JsonConfigStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonConfigStore> logger) : IConfigStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:ConfigFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "configuracion.json");

    private Dictionary<string, string> Leer()
    {
        if (!File.Exists(_path)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path), Json) ?? new(); }
        catch (Exception e) { logger.LogWarning("No se pudo leer configuracion.json: {Error}", e.Message); return new(); }
    }

    public string Get(string clave, string predeterminado)
    {
        lock (_lock) { return Leer().TryGetValue(clave, out var v) && v.Length > 0 ? v : predeterminado; }
    }

    public (int, string?) Set(string clave, string valor)
    {
        lock (_lock)
        {
            var datos = Leer();
            datos[clave] = valor;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(datos, Json));
            }
            catch (Exception e) { logger.LogWarning("No se pudo guardar configuracion.json: {Error}", e.Message); return (500, "No se pudo guardar la configuración."); }
            return (204, null);
        }
    }

    public IReadOnlyDictionary<string, string> Todo()
    {
        lock (_lock)
        {
            var datos = ConfigKeys.Defaults.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var (k, v) in Leer()) datos[k] = v;
            return datos;
        }
    }
}

public class MySqlConfigStore(IConfiguration config) : IConfigStore
{
    private readonly string _cs = config.GetConnectionString("Usuarios")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Usuarios en la configuracion.");

    private MySqlConnection Conexion() => new(_cs);

    public void EnsureSchema()
    {
        var db = new MySqlConnectionStringBuilder(_cs).Database;
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{db}`.`configuracion` (
              `clave` VARCHAR(60)  NOT NULL PRIMARY KEY,
              `valor` VARCHAR(255) NOT NULL DEFAULT ''
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
        // Los catálogos del Programador se guardan como JSON y rebasan los 255 caracteres con los
        // que se creó la tabla: la columna pasa a TEXT (conversión sin pérdida de lo ya guardado).
        // Es idempotente y si falla se sigue usando la columna antigua; no debe tumbar el arranque.
        using var ancho = cn.CreateCommand();
        ancho.CommandText = $"ALTER TABLE `{db}`.`configuracion` MODIFY COLUMN `valor` TEXT NOT NULL";
        try { ancho.ExecuteNonQuery(); }
        catch (Exception e) { Console.WriteLine($"[config] no se pudo ampliar configuracion.valor: {e.Message}"); }
    }

    public string Get(string clave, string predeterminado)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT valor FROM configuracion WHERE clave = @c";
            cmd.Parameters.AddWithValue("@c", clave);
            var v = cmd.ExecuteScalar() as string;
            return string.IsNullOrEmpty(v) ? predeterminado : v;
        }
        catch { return predeterminado; }
    }

    public (int, string?) Set(string clave, string valor)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO configuracion (clave, valor) VALUES (@c, @v)
                ON DUPLICATE KEY UPDATE valor = @v
                """;
            cmd.Parameters.AddWithValue("@c", clave);
            cmd.Parameters.AddWithValue("@v", valor ?? "");
            cmd.ExecuteNonQuery();
            return (204, null);
        }
        catch { return (500, "No se pudo guardar la configuración."); }
    }

    public IReadOnlyDictionary<string, string> Todo()
    {
        var datos = ConfigKeys.Defaults.ToDictionary(kv => kv.Key, kv => kv.Value);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT clave, valor FROM configuracion";
            using var r = cmd.ExecuteReader();
            while (r.Read()) datos[r.GetString(0)] = r.GetString(1);
        }
        catch { /* se queda con los defaults */ }
        return datos;
    }
}
