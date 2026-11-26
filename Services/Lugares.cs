using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// <summary>
/// Un lugar reportado por el propio equipo que abrió la intranet. Es una foto del momento:
/// dice en qué coordenadas estaba esa sesión y con qué dirección legible se tradujeron.
/// </summary>
/// <param name="Usuario">Cuenta iniciada en ese momento.</param>
/// <param name="Ip">IP de la conexión (la de la red local del hospital).</param>
/// <param name="Momento">Cuándo se reportó (UTC).</param>
/// <param name="Latitud">Latitud en grados decimales.</param>
/// <param name="Longitud">Longitud en grados decimales.</param>
/// <param name="PrecisionMetros">Radio aproximado del dato; 0 cuando no lo sabe quien lo midió.</param>
/// <param name="Direccion">Dirección legible («Vallejo, Gustavo A. Madero, 07870 Ciudad de México»).</param>
/// <param name="Fuente">De dónde salieron las coordenadas: <c>ip</c> (aproximada por la salida a
/// Internet del equipo) o <c>navegador</c> (GPS/Wi-Fi del propio aparato).</param>
public record LugarReportado(string Usuario, string Ip, DateTime Momento, double Latitud, double Longitud,
    int PrecisionMetros, string Direccion, string Fuente);

/// <summary>Guarda los lugares reportados y devuelve el más reciente por IP.</summary>
public interface ILugarStore
{
    void Guardar(LugarReportado lugar);

    /// <summary>Último lugar conocido de cada IP vista por la intranet (los más nuevos primero).</summary>
    IReadOnlyDictionary<string, LugarReportado> UltimosPorIp(int limiteDias = 60);
}

/// <summary>Versión en archivo (<c>App_Data/lugares.jsonl</c>), para cuando la intranet corre sin MySQL.</summary>
public class JsonLugarStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonLugarStore> logger)
    : ILugarStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:LugaresFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "lugares.jsonl");

    public void Guardar(LugarReportado lugar)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, JsonSerializer.Serialize(lugar, Json) + Environment.NewLine);
            }
        }
        catch (Exception e) { logger.LogWarning("No se pudo guardar lugares.jsonl: {Error}", e.Message); }
    }

    public IReadOnlyDictionary<string, LugarReportado> UltimosPorIp(int limiteDias = 60)
    {
        var salida = new Dictionary<string, LugarReportado>(StringComparer.OrdinalIgnoreCase);
        string[] lineas;
        try
        {
            lock (_lock) lineas = File.Exists(_path) ? File.ReadAllLines(_path) : Array.Empty<string>();
        }
        catch (Exception e) { logger.LogWarning("No se pudo leer lugares.jsonl: {Error}", e.Message); return salida; }

        var corte = DateTime.UtcNow.AddDays(-limiteDias);
        foreach (var linea in lineas)
        {
            if (linea.Trim().Length == 0) continue;
            LugarReportado? l;
            try { l = JsonSerializer.Deserialize<LugarReportado>(linea, Json); } catch { continue; }
            if (l is null || l.Momento < corte || l.Ip.Length == 0) continue;
            if (salida.TryGetValue(l.Ip, out var previo) && previo.Momento >= l.Momento) continue;
            salida[l.Ip] = l;
        }
        return salida;
    }
}

/// <summary>Versión en MySQL: tabla <c>bitacora_lugar</c>, un renglón por reporte.</summary>
public class MySqlLugarStore(IConfiguration config, ILogger<MySqlLugarStore> logger) : ILugarStore
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
            CREATE TABLE IF NOT EXISTS `{db}`.`bitacora_lugar` (
              `id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
              `momento` DATETIME(6) NOT NULL,
              `usuario` VARCHAR(40) NOT NULL DEFAULT '',
              `ip` VARCHAR(45) NOT NULL DEFAULT '',
              `latitud` DECIMAL(9,6) NOT NULL DEFAULT 0,
              `longitud` DECIMAL(9,6) NOT NULL DEFAULT 0,
              `precision_metros` SMALLINT NOT NULL DEFAULT 0,
              `direccion` VARCHAR(200) NOT NULL DEFAULT '',
              `fuente` VARCHAR(20) NOT NULL DEFAULT 'ip',
              KEY `ix_lugar_ip` (`ip`, `momento`)
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public void Guardar(LugarReportado lugar)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO bitacora_lugar (momento, usuario, ip, latitud, longitud, precision_metros, direccion, fuente)
                VALUES (@m, @u, @i, @la, @lo, @p, @d, @f)
                """;
            cmd.Parameters.AddWithValue("@m", lugar.Momento);
            cmd.Parameters.AddWithValue("@u", Recorta(lugar.Usuario, 40));
            cmd.Parameters.AddWithValue("@i", Recorta(lugar.Ip, 45));
            cmd.Parameters.AddWithValue("@la", lugar.Latitud);
            cmd.Parameters.AddWithValue("@lo", lugar.Longitud);
            cmd.Parameters.AddWithValue("@p", lugar.PrecisionMetros);
            cmd.Parameters.AddWithValue("@d", Recorta(lugar.Direccion, 200));
            cmd.Parameters.AddWithValue("@f", Recorta(lugar.Fuente, 20));
            cmd.ExecuteNonQuery();
        }
        catch (Exception e) { logger.LogWarning("No se pudo guardar el lugar reportado: {Error}", e.Message); }
    }

    public IReadOnlyDictionary<string, LugarReportado> UltimosPorIp(int limiteDias = 60)
    {
        var salida = new Dictionary<string, LugarReportado>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                SELECT l.ip, l.momento, l.usuario, l.latitud, l.longitud, l.precision_metros, l.direccion, l.fuente
                  FROM bitacora_lugar l
                  JOIN (SELECT ip, MAX(id) id_max FROM bitacora_lugar GROUP BY ip) u ON u.id_max = l.id
                 WHERE l.direccion <> '' AND l.momento >= @corte
                """;
            cmd.Parameters.AddWithValue("@corte", DateTime.UtcNow.AddDays(-limiteDias));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var l = new LugarReportado(
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    r.IsDBNull(0) ? "" : r.GetString(0),
                    r.GetDateTime(1),
                    (double)r.GetDecimal(3),
                    (double)r.GetDecimal(4),
                    r.GetInt16(5),
                    r.IsDBNull(6) ? "" : r.GetString(6),
                    r.IsDBNull(7) ? "" : r.GetString(7));
                if (l.Ip.Length > 0) salida[l.Ip] = l;
            }
        }
        catch (Exception e) { logger.LogWarning("No se pudieron leer los lugares reportados: {Error}", e.Message); }
        return salida;
    }

    private static string Recorta(string? s, int max) => (s ?? "").Length <= max ? (s ?? "") : s![..max];
}

/// <summary>
/// Traduce coordenadas a una dirección legible («Vallejo, Gustavo A. Madero, 07870 Ciudad de México»).
///
/// Usa OpenStreetMap (Nominatim) porque es el único servicio gratuito que devuelve colonia, alcaldía,
/// código postal y ciudad por separado; Google pide clave de pago. Se cumplen sus reglas de uso:
/// una petición por segundo como máximo, identificación clara en el User-Agent y caché de 30 días,
/// así que un mismo punto del mapa solo se pregunta una vez al mes.
///
/// Si el servicio no contesta (no hay salida a Internet, o tarda), la pantalla NO se cuelga: se queda
/// con lo que haya y reintenta mas adelante.
/// </summary>
public class GeocodificadorLugar(IHttpClientFactory httpFactory, ILogger<GeocodificadorLugar> logger)
{
    /// <summary>Lo que se espera por una respuesta antes de rendirse.</summary>
    private static readonly TimeSpan Presupuesto = TimeSpan.FromSeconds(4);

    /// <summary>Un punto ya traducido se recuerda este tiempo (no se vuelve a preguntar por él).</summary>
    private static readonly TimeSpan VidaCache = TimeSpan.FromDays(30);

    /// <summary>Cuánto se espera antes de reintentar un punto que falló.</summary>
    private static readonly TimeSpan VidaFallo = TimeSpan.FromHours(6);

    /// <summary>Regla de cortesía del servicio: máximo una petición por segundo.</summary>
    private static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMilliseconds(1100);

    private readonly ConcurrentDictionary<string, (PuntoLeido Punto, DateTime Caduca)> _cache = new();
    private readonly SemaphoreSlim _turno = new(1, 1);
    private DateTime _ultimaPeticion = DateTime.MinValue;

    /// <summary>
    /// Dirección del punto, o cadena vacía si no se pudo obtener. Nunca lanza: la bitácora se muestra
    /// igual aunque Internet no responda.
    /// </summary>
    public async Task<string> DireccionAsync(double latitud, double longitud, CancellationToken ct = default)
        => (await LeerAsync(latitud, longitud, exacta: false, ct)).Direccion;

    /// <summary>
    /// Dirección <b>completa</b> del punto que el administrador marcó: calle y número, colonia, código
    /// postal, alcaldía y ciudad. Es la que se guarda como ubicación exacta del sitio, porque una falta
    /// se justifica con una dirección postal, no con el nombre de una colonia.
    /// </summary>
    public async Task<string> DireccionExactaAsync(double latitud, double longitud, CancellationToken ct = default)
        => (await LeerAsync(latitud, longitud, exacta: true, ct)).Direccion;

    /// <summary>
    /// Lo que el mapa sabe de un punto exacto: la dirección completa y, si el punto cayó sobre un
    /// edificio con nombre (un hospital, una torre), ese nombre. Lo usa la vista previa del mapa para
    /// que el administrador vea qué se va a guardar <b>antes</b> de guardarle.
    /// </summary>
    public Task<PuntoLeido> LeerExactoAsync(double latitud, double longitud, CancellationToken ct = default)
        => LeerAsync(latitud, longitud, exacta: true, ct);

    /// <summary>
    /// Qué se leyó de un punto. <paramref name="Direccion"/> queda vacía si el mapa no contestó.
    /// <paramref name="Edificio"/> solo trae algo cuando el punto cayó sobre un sitio con nombre.
    /// </summary>
    public readonly record struct PuntoLeido(string Direccion, string Edificio);

    private async Task<PuntoLeido> LeerAsync(double latitud, double longitud, bool exacta, CancellationToken ct)
    {
        var vacio = new PuntoLeido("", "");
        if (!EsCoordenada(latitud, longitud)) return vacio;

        // Se agrupan las coordenadas al cuarto decimal (~11 metros): dos reportes del mismo escritorio
        // aprovechan la misma respuesta en lugar de preguntar dos veces. El modo (aproximado o exacto)
        // va en la clave porque son dos lecturas distintas del mismo punto.
        var clave = $"{Math.Round(latitud, 4):0.0000},{Math.Round(longitud, 4):0.0000}|{(exacta ? "e" : "a")}";
        var ahora = DateTime.UtcNow;
        if (_cache.TryGetValue(clave, out var guardado))
        {
            if (guardado.Caduca > ahora) return guardado.Punto;
        }

        var leido = vacio;
        try
        {
            await _turno.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Respeta el turno de una petición por segundo entre todos los usuarios.
                var espera = IntervaloMinimo - (DateTime.UtcNow - _ultimaPeticion);
                if (espera > TimeSpan.Zero) await Task.Delay(espera, ct).ConfigureAwait(false);
                _ultimaPeticion = DateTime.UtcNow;

                var http = httpFactory.CreateClient(nameof(GeocodificadorLugar));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(Presupuesto);
                // zoom 18 = hasta el número del edificio; zoom 16 = la zona, que es lo que alcanza para
                // la deducción por IP (y lo honesto: una IP no sabe en qué edificio está el aparato).
                var url = "https://nominatim.openstreetmap.org/reverse?format=jsonv2&addressdetails=1"
                    + "&zoom=" + (exacta ? 18 : 16)
                    + "&lat=" + latitud.ToString("0.000000", CultureInfo.InvariantCulture)
                    + "&lon=" + longitud.ToString("0.000000", CultureInfo.InvariantCulture);
                using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
                peticion.Headers.UserAgent.ParseAdd("Meditechnosys-Intranet/1.0 (bitacora interna de accesos)");
                var respuesta = await http.SendAsync(peticion, cts.Token).ConfigureAwait(false);
                if (respuesta.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
                    if (exacta)
                    {
                        var (dir, edificio) = DesmenuzaExacta(doc.RootElement);
                        leido = new PuntoLeido(dir, edificio);
                    }
                    else leido = new PuntoLeido(Formatea(doc.RootElement), "");
                }
                else
                {
                    logger.LogDebug("Nominatim respondió {Codigo} para {Punto}", (int)respuesta.StatusCode, clave);
                }
            }
            finally { _turno.Release(); }
        }
        catch (Exception e) { logger.LogDebug(e, "No se pudo traducir la coordenada {Punto}", clave); }

        _cache[clave] = (leido, leido.Direccion.Length > 0 ? DateTime.UtcNow.Add(VidaCache) : DateTime.UtcNow.Add(VidaFallo));
        return leido;
    }

    /// <summary>
    /// Arma «colonia, alcaldía, código postal ciudad, estado» con lo que devolvió el mapa, saltando lo
    /// que falte y sin repetir la ciudad cuando también viene como estado.
    /// </summary>
    public static string Formatea(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("address", out var a)) return "";
        string C(string clave) => a.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Trim() : "";

        var colonia = PrimerNoVacio(C("neighbourhood"), C("suburb"), C("quarter"), C("city_block"), C("road"));
        var alcaldia = PrimerNoVacio(C("borough"), C("city_district"), C("municipality"));
        var cp = C("postcode");
        var ciudad = PrimerNoVacio(C("city"), C("town"), C("village"), C("municipio"));
        var estado = PrimerNoVacio(C("state"), C("region"));

        var partes = new List<string>();
        if (colonia.Length > 0) partes.Add(colonia);
        if (alcaldia.Length > 0 && !MismoLugar(alcaldia, colonia)) partes.Add(alcaldia);

        // «07870 Ciudad de México» se lee junto, como en una dirección postal.
        var cpCiudad = (cp.Length > 0 ? cp + " " : "") + (ciudad.Length > 0 && !MismoLugar(ciudad, alcaldia) ? ciudad : "");
        if (cpCiudad.Trim().Length > 0) partes.Add(cpCiudad.Trim());
        if (estado.Length > 0 && !MismoLugar(estado, ciudad) && !MismoLugar(estado, alcaldia)) partes.Add(estado);

        var salida = string.Join(", ", partes);
        return salida.Length > 200 ? salida[..200].TrimEnd(',', ' ') : salida;
    }

    /// <summary>
    /// Dirección <b>postal completa</b> del punto marcado: calle con su número, colonia, código postal,
    /// alcaldía, ciudad y estado —«Ejido 26, Col. San Bartolo Atepehuacan, C.P. 07730, Gustavo A.
    /// Madero, Ciudad de México»— y, si el punto cayó encima de un sitio con nombre (una UMF, un
    /// hospital, una torre), ese nombre abre la dirección y también se devuelve aparte.
    ///
    /// Es la diferencia entre «Vallejo» (la zona, que sirve para deducir) y una dirección con la que se
    /// puede sustentar una falta ante Recursos Humanos.
    ///
    /// Solo se usa con la lectura exacta (<c>zoom=18</c>): pedirle calle y número a una coordenada que
    /// salió de una IP daría una dirección que parece finísima pero es mentira, y eso no justifica nada.
    /// </summary>
    /// <param name="raiz">Respuesta de Nominatim con <c>addressdetails=1</c> y <c>zoom=18</c>.</param>
    /// <returns>La dirección legible (vacía si el mapa no trajo nada aprovechable) y el nombre del edificio.</returns>
    public static (string Direccion, string Edificio) DesmenuzaExacta(JsonElement raiz)
    {
        static string TextoDe(JsonElement e, string clave) =>
            e.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

        if (!raiz.TryGetProperty("address", out var a)) return ("", "");
        string C(string clave) => TextoDe(a, clave);

        var nombre = TextoDe(raiz, "name");

        // Calle y número van juntos, como en un sobre: «Ejido 26». No se le pone «Calle» por delante:
        // el mapa ya trae el nombre completo («Av. Río Churubusco», «Cda. de las Palmas») y agregarlo
        // saldría mal la mitad de las veces.
        var calle = PrimerNoVacio(C("road"), C("pedestrian"), C("footway"), C("path"));
        var numero = C("house_number");
        var via = (calle + (numero.Length > 0 ? " " + numero : "")).Trim();

        // Colonia: en CDMX viene como neighbourhood; en otros lugares, como suburb o quarter.
        var colonia = PrimerNoVacio(C("neighbourhood"), C("suburb"), C("quarter"), C("city_block"));
        if (colonia.Length > 0 && !EmpiezaConColonia(colonia)) colonia = "Col. " + colonia;

        var cp = C("postcode");
        var cpTexto = cp.Length > 0 ? "C.P. " + cp : "";

        var alcaldia = PrimerNoVacio(C("borough"), C("city_district"), C("municipality"), C("district"));
        var ciudad = PrimerNoVacio(C("city"), C("town"), C("village"), C("municipio"));
        var estado = PrimerNoVacio(C("state"), C("region"));
        var pais = C("country");

        var partes = new List<string>();
        // El nombre abre la dirección SOLO cuando el mapa reconoció un sitio con nombre (una clínica,
        // un hospital, un edificio), no cuando lo que encontró fue una zona, un límite o una simple
        // calle: «Calle Jesús Carranza» viene como nombre de la calle y repetirla arriba no aporta.
        var categoria = TextoDe(raiz, "category");   // amenity | building | highway | place | boundary …
        var edificio = nombre.Length > 0 && EsSitioConNombre(categoria) ? nombre : "";
        if (edificio.Length > 0) partes.Add(edificio);
        if (via.Length > 0) partes.Add(via);
        if (colonia.Length > 0) partes.Add(colonia);
        if (cpTexto.Length > 0) partes.Add(cpTexto);
        if (alcaldia.Length > 0) partes.Add(alcaldia);
        if (ciudad.Length > 0) partes.Add(ciudad);
        // «Ciudad de México» llega como estado y casi nunca como ciudad; si llegan las dos iguales, se
        // escribe una sola vez (UneSinRepetir se encarga).
        if (estado.Length > 0) partes.Add(estado);
        // El país se omite cuando es México —el hospital entero está en México— para no alargar la columna.
        if (pais.Length > 0 && !EsMexico(pais)) partes.Add(pais);

        var completa = UneSinRepetir(partes);
        // 200 caracteres es lo que mide la columna donde se guarda; pasar de ahí cortaría el texto.
        if (completa.Length > 200) completa = completa[..200].TrimEnd(',', ' ');
        return (completa, edificio);
    }

    /// <summary>«Col. San Bartolo» o «Colonia San Bartolo» ya traen la palabra: no se les vuelve a poner.</summary>
    private static bool EmpiezaConColonia(string colonia) =>
        Texto.Empieza(colonia, "col ") || Texto.Empieza(colonia, "col.") || Texto.Empieza(colonia, "colonia");

    /// <summary>
    /// ¿El mapa dijo que el punto cayó en un sitio con nombre? Se mira la categoría que devuelve
    /// OpenStreetMap: los lugares que se nombran (una unidad médica, un edificio, una escuela) vienen en
    /// estos grupos; «highway», «place» o «boundary» son calles, zonas o lindes, cuyo «nombre» es el de la
    /// calle o el de la colonia, y repetirlo al principio de la dirección solo confundiría.
    /// </summary>
    private static bool EsSitioConNombre(string categoria) => categoria is
        "amenity" or "building" or "shop" or "tourism" or "office" or "healthcare" or "educational"
        or "leisure" or "public" or "railway" or "aeroway" or "man_made";

    /// <summary>México es el país de todo el hospital; repetirlo en cada renglón solo estorba.</summary>
    private static bool EsMexico(string pais) =>
        Texto.Normalizar(pais) == "MEXICO" || Texto.Empieza(pais, "united mexican");

    /// <summary>Quita los fragmentos repetidos (la misma alcaldía que llegó como ciudad) y une con comas.</summary>
    private static string UneSinRepetir(List<string> partes)
    {
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        var salida = new List<string>();
        foreach (var p in partes)
        {
            var s = (p ?? "").Trim().TrimEnd(',');
            if (s.Length == 0 || !vistas.Add(Texto.Normalizar(s))) continue;
            salida.Add(s);
        }
        return string.Join(", ", salida);
    }

    /// <summary>Coordenada dentro de lo posible y no en el punto cero (0,0 es «no se midió»).</summary>
    public static bool EsCoordenada(double latitud, double longitud) =>
        latitud is >= -90 and <= 90 && longitud is >= -180 and <= 180 &&
        !(Math.Abs(latitud) < 0.0001 && Math.Abs(longitud) < 0.0001);

    private static string PrimerNoVacio(params string[] valores)
    {
        foreach (var v in valores) if (v.Trim().Length > 0) return v.Trim();
        return "";
    }

    /// <summary>«Gustavo A. Madero» y «Gustavo A. Madero» son el mismo lugar aunque uno traiga acentos.</summary>
    private static bool MismoLugar(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        return Texto.Normalizar(a) == Texto.Normalizar(b);
    }
}

/// <summary>
/// El punto que el administrador marcó en el mapa del panel. Es la única forma de que la dirección del
/// sitio salga <b>exacta</b>: lo que geolocaliza un servicio por IP es el equipo del proveedor de
/// Internet, no el edificio del hospital, así que sin este punto la dirección siempre es aproximada.
/// </summary>
/// <param name="Direccion">Dirección legible que salió de ese punto (o la que el administrador escribió).</param>
/// <param name="Latitud">Latitud marcada (0 si solo se escribió la dirección, sin punto).</param>
/// <param name="Longitud">Longitud marcada.</param>
/// <param name="ActualizadoEn">Cuándo se marcó (UTC).</param>
/// <param name="ActualizadoPor">Cuenta que lo marcó, para dejar constancia en la bitácora.</param>
public sealed record SitioCalibrado(string Direccion, double Latitud, double Longitud,
    DateTime ActualizadoEn, string ActualizadoPor);

/// <summary>
/// Guardado del punto calibrado. Como mucho hay uno: el de la intranet entera (los puestos concretos se
/// siguen escribiendo por rango de IP en <see cref="IUbicacionStore"/>).
/// </summary>
public interface ISitioStore
{
    /// <summary>El punto marcado, o null si la intranet sigue en modo automático.</summary>
    SitioCalibrado? Leer();

    void Guardar(SitioCalibrado sitio);

    /// <returns>false si no había nada que quitar.</returns>
    bool Borrar();
}

/// <summary>Versión en archivo (<c>App_Data/sitio.json</c>), para cuando la intranet corre sin MySQL.</summary>
public class JsonSitioStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonSitioStore> logger)
    : ISitioStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:SitioFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "sitio.json");

    public SitioCalibrado? Leer()
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return null;
                return JsonSerializer.Deserialize<SitioCalibrado>(File.ReadAllText(_path), Json);
            }
        }
        catch (Exception e) { logger.LogWarning("No se pudo leer sitio.json: {Error}", e.Message); return null; }
    }

    public void Guardar(SitioCalibrado sitio)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(sitio, Json));
            }
        }
        catch (Exception e) { logger.LogWarning("No se pudo guardar sitio.json: {Error}", e.Message); }
    }

    public bool Borrar()
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return false;
                File.Delete(_path);
                return true;
            }
        }
        catch (Exception e) { logger.LogWarning("No se pudo quitar sitio.json: {Error}", e.Message); return false; }
    }
}

/// <summary>Versión en MySQL: un renglón fijo en <c>bitacora_sitio</c>.</summary>
public class MySqlSitioStore(IConfiguration config, ILogger<MySqlSitioStore> logger) : ISitioStore
{
    private const long Fijo = 1;
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
            CREATE TABLE IF NOT EXISTS `{db}`.`bitacora_sitio` (
              `id` TINYINT NOT NULL PRIMARY KEY,
              `direccion` VARCHAR(200) NOT NULL DEFAULT '',
              `latitud` DECIMAL(9,6) NOT NULL DEFAULT 0,
              `longitud` DECIMAL(9,6) NOT NULL DEFAULT 0,
              `actualizado_en` DATETIME(6) NOT NULL,
              `actualizado_por` VARCHAR(40) NOT NULL DEFAULT ''
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public SitioCalibrado? Leer()
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT direccion, latitud, longitud, actualizado_en, actualizado_por FROM bitacora_sitio WHERE id = @i";
            cmd.Parameters.AddWithValue("@i", Fijo);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            return new SitioCalibrado(
                r.IsDBNull(0) ? "" : r.GetString(0),
                (double)r.GetDecimal(1),
                (double)r.GetDecimal(2),
                r.GetDateTime(3),
                r.IsDBNull(4) ? "" : r.GetString(4));
        }
        catch (Exception e)
        {
            logger.LogWarning("No se pudo leer la calibración del sitio: {Error}", e.Message);
            return null;
        }
    }

    public void Guardar(SitioCalibrado sitio)
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bitacora_sitio (id, direccion, latitud, longitud, actualizado_en, actualizado_por)
            VALUES (@i, @d, @la, @lo, @m, @u)
            ON DUPLICATE KEY UPDATE direccion = @d, latitud = @la, longitud = @lo,
                                    actualizado_en = @m, actualizado_por = @u
            """;
        cmd.Parameters.AddWithValue("@i", Fijo);
        cmd.Parameters.AddWithValue("@d", sitio.Direccion);
        cmd.Parameters.AddWithValue("@la", sitio.Latitud);
        cmd.Parameters.AddWithValue("@lo", sitio.Longitud);
        cmd.Parameters.AddWithValue("@m", sitio.ActualizadoEn);
        cmd.Parameters.AddWithValue("@u", sitio.ActualizadoPor);
        cmd.ExecuteNonQuery();
    }

    public bool Borrar()
    {
        using var cn = Conexion();
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM bitacora_sitio WHERE id = @i";
        cmd.Parameters.AddWithValue("@i", Fijo);
        return cmd.ExecuteNonQuery() > 0;
    }
}

/// <summary>
/// La dirección geográfica del lugar donde está la intranet, con la constancia de cómo se obtuvo.
/// </summary>
/// <param name="Direccion">Dirección legible («Vallejo, Gustavo A. Madero, 07870 Ciudad de México»).</param>
/// <param name="Latitud">Latitud del punto medido.</param>
/// <param name="Longitud">Longitud del punto medido.</param>
/// <param name="Fuente"><c>calibrado</c>: el punto que marcó el administrador en el mapa (el dato exacto);
/// <c>servidor</c>: medida por la propia intranet al salir a Internet;
/// <c>equipos</c>: la dirección más repetida entre lo que ya reportaron los aparatos conectados.</param>
/// <param name="MomentoUtc">Cuándo se midió o se calibró (UTC).</param>
/// <param name="ActualizadoPor">Quien marcó el punto; vacío cuando el dato no es calibrado.</param>
public sealed record DatoSitio(string Direccion, double Latitud, double Longitud, string Fuente,
    DateTime MomentoUtc, string ActualizadoPor = "");

/// <summary>
/// La dirección geográfica del sitio. Primero la que **calibró el administrador** marcando el edificio
/// en el mapa (`bitacora_sitio`), que es la única exacta; y mientras no haya calibración, la procura
/// <b>sola</b> para que la columna Ubicación de la bitácora nunca quede en blanco.
///
/// Sin calibrar, cómo lo hace: el servidor pregunta a su propia salida a Internet en qué coordenadas
/// está (los mismos servicios gratuitos que usa <c>wwwroot/js/ubicacion.js</c>: ipapi.co, y si no,
/// ipwho.is, y si no, freeipapi.com) y traduce el punto con <see cref="GeocodificadorLugar"/>. El
/// resultado se recuerda seis horas y se vuelve a medir en segundo plano, nunca mientras la pantalla
/// espera. Ojo: ese dato es <b>aproximado y suele fallar de zona</b>, porque lo que geolocaliza el
/// proveedor es el equipo del operador de Internet, no el hospital —de ahí que exista la calibración—.
///
/// Si el servidor tampoco tiene salida a Internet —lo normal en una intranet aislada— se aprovecha la
/// dirección más repetida entre lo que ya reportaron los equipos.
///
/// Nada de esto puede colgar una pantalla: la primera consulta lanza la medición y devuelve lo que se
/// sepa en ese instante; desde la siguiente consulta ya siempre aparece la dirección. Con calibración
/// puesta no se hace ninguna consulta a Internet.
/// </summary>
public class LugarDelSitio(IHttpClientFactory httpFactory, GeocodificadorLugar geocodificador,
    ILugarStore lugares, ISitioStore calibracion, ILogger<LugarDelSitio> logger)
{
    /// <summary>Lo que dura una dirección medida; después se refresca sola, en segundo plano.</summary>
    private static readonly TimeSpan VidaDato = TimeSpan.FromHours(6);

    /// <summary>Cuánto se espera antes de volver a preguntar cuando ningún servicio respondió.</summary>
    private static readonly TimeSpan VidaIntentoFallido = TimeSpan.FromMinutes(20);

    /// <summary>Cada cuánto se rehace la deducción a partir de los reportes de los equipos.</summary>
    private static readonly TimeSpan VidaDeducción = TimeSpan.FromMinutes(5);

    /// <summary>Lo que se espera por la respuesta de un servicio antes de probar el siguiente.</summary>
    private static readonly TimeSpan Presupuesto = TimeSpan.FromSeconds(5);

    /// <summary>Cada cuánto se vuelve a leer la calibración escrita por el administrador.</summary>
    private static readonly TimeSpan VidaCalibración = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Servicios que dicen «qué coordenadas tiene esta salida a Internet», en el orden en que se prueban.
    /// Basta con que uno conteste. Las columnas de texto varían entre ellos, por eso se listan por nombre.
    /// </summary>
    private static readonly (string Url, string Latitud, string Longitud, string Ciudad, string Estado, string CP)[]
        Proveedores =
    [
        ("https://ipapi.co/json/", "latitude", "longitude", "city", "region", "postal"),
        ("https://ipwho.is/", "latitude", "longitude", "city", "region", "postal"),
        ("https://freeipapi.com/api/json", "latitude", "longitude", "cityName", "regionName", "zipCode"),
    ];

    private readonly object _gate = new();
    private volatile DatoSitio? _dato;
    private DateTime _caduca = DateTime.MinValue;
    private DatoSitio? _deducido;
    private DateTime _caducaDeduccion = DateTime.MinValue;
    private volatile SitioCalibrado? _calibrado;
    private DateTime _caducaCalibracion = DateTime.MinValue;

    /// <summary>0 = nadie midiendo ahora; 1 = ya hay una tarea en vuelo (así no se duplican).</summary>
    private int _enVuelo;

    /// <summary>
    /// La dirección del sitio: la calibrada si el administrador marcó el edificio; si no, la medida por
    /// el servidor; y si esa falta, la que se deduce de los equipos. Cadena vacía cuando todavía no se
    /// sabe nada (la primera vez, o sin Internet ni reportes).
    /// </summary>
    public string Direccion => Actual()?.Direccion ?? "";

    /// <summary>Dato completo, con origen y fecha. Null si aún no se sabe dónde está el sitio.</summary>
    public DatoSitio? Actual()
    {
        var fijado = Calibrado();
        if (fijado is not null && fijado.Direccion.Trim().Length > 0)
            return new DatoSitio(fijado.Direccion.Trim(), fijado.Latitud, fijado.Longitud,
                "calibrado", fijado.ActualizadoEn, fijado.ActualizadoPor);

        var ahora = DateTime.UtcNow;
        var medido = _dato;
        if (medido is null || medido.Direccion.Length == 0 || _caduca <= ahora)
        {
            // Se mide en segundo plano: esta consulta no espera a Internet.
            RefrescaEnSegundoPlano();
            medido = _dato;
        }
        if (medido is not null && medido.Direccion.Length > 0) return medido;
        return DeducidoDeEquipos(ahora);
    }

    /// <summary>El punto que marcó el administrador, o null si la intranet sigue en modo automático.</summary>
    public SitioCalibrado? Calibrado()
    {
        var ahora = DateTime.UtcNow;
        lock (_gate)
        {
            if (_caducaCalibracion > ahora) return _calibrado;
            try { _calibrado = calibracion.Leer(); }
            catch (Exception e) { logger.LogDebug(e, "No se pudo leer la calibración del sitio"); }
            _caducaCalibracion = ahora.Add(VidaCalibración);
            return _calibrado;
        }
    }

    /// <summary>
    /// Se olvida de lo que había en memoria para que la calibración nueva (o quitada) se note al
    /// instante en la bitácora, sin esperar a que venza la caché.
    /// </summary>
    public void InvalidaCalibracion()
    {
        lock (_gate)
        {
            _caducaCalibracion = DateTime.MinValue;
            _calibrado = null;
            _caduca = DateTime.MinValue;
            _caducaDeduccion = DateTime.MinValue;
        }
        // Con calibración no hace falta medir nada; sin ella, se mide sola en segundo plano.
        RefrescaEnSegundoPlano();
    }

    /// <summary>Mide otra vez en un hilo aparte, si nadie lo está haciendo ya y no hay calibración.</summary>
    public void RefrescaEnSegundoPlano()
    {
        if (Calibrado() is not null) return;      // el punto marcado manda: no se gasta la cuota de nadie
        if (Interlocked.CompareExchange(ref _enVuelo, 1, 0) != 0) return;
        _ = Task.Run(MedirAsync);
    }

    /// <summary>
    /// Pregunta a los servicios de geolocalización por la salida a Internet del servidor y traduce el
    /// punto a dirección. Nunca lanza: si nada responde, deja lo que hubiera y pospone el reintento.
    /// </summary>
    private async Task MedirAsync()
    {
        var obtuvo = false;
        try
        {
            var http = httpFactory.CreateClient(nameof(LugarDelSitio));
            foreach (var p in Proveedores)
            {
                try
                {
                    using var cts = new CancellationTokenSource(Presupuesto);
                    using var respuesta = await http.GetAsync(p.Url, cts.Token).ConfigureAwait(false);
                    if (!respuesta.IsSuccessStatusCode) continue;

                    using var doc = JsonDocument.Parse(
                        await respuesta.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
                    var raiz = doc.RootElement;
                    // ipwho.is contesta 200 con {"success":false} cuando no reconoce la dirección.
                    if (raiz.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) continue;

                    var lat = Numero(raiz, p.Latitud);
                    var lon = Numero(raiz, p.Longitud);
                    if (!GeocodificadorLugar.EsCoordenada(lat, lon)) continue;

                    var texto = Une(Cadena(raiz, p.Ciudad), Cadena(raiz, p.Estado), Cadena(raiz, p.CP));

                    // El mapa da la forma legible (colonia, alcaldía, código postal); si no contesta,
                    // se queda uno con el texto breve del propio servicio, que es peor pero algo es.
                    var direccion = await geocodificador.DireccionAsync(lat, lon, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (direccion.Length == 0) direccion = texto;
                    if (direccion.Length == 0) continue;
                    if (direccion.Length > 200) direccion = direccion[..200];

                    _dato = new DatoSitio(direccion, lat, lon, "servidor", DateTime.UtcNow);
                    obtuvo = true;
                    break;
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "El servicio de geolocalización {Url} no respondió", p.Url);
                }
            }
        }
        finally
        {
            _caduca = DateTime.UtcNow.Add(obtuvo ? VidaDato : VidaIntentoFallido);
            Interlocked.Exchange(ref _enVuelo, 0);
        }
    }

    /// <summary>
    /// Plan B sin Internet: la dirección que más se repite entre las que reportaron los aparatos. Todos
    /// salen por el mismo proveedor, así que coinciden en la zona del edificio; la que más se repite es
    /// la que mejor lo representa.
    /// </summary>
    private DatoSitio? DeducidoDeEquipos(DateTime ahora)
    {
        lock (_gate)
        {
            if (_caducaDeduccion > ahora) return _deducido;
            _caducaDeduccion = ahora.Add(VidaDeducción);
            try
            {
                var porLugar = new Dictionary<string, (int Usos, LugarReportado MasReciente)>();
                foreach (var l in lugares.UltimosPorIp().Values)
                {
                    if (l.Direccion.Trim().Length == 0) continue;
                    var clave = Texto.Normalizar(l.Direccion);
                    if (porLugar.TryGetValue(clave, out var previo))
                    {
                        porLugar[clave] = (previo.Usos + 1, previo.MasReciente.Momento >= l.Momento ? previo.MasReciente : l);
                    }
                    else porLugar[clave] = (1, l);
                }
                _deducido = null;
                var mejor = porLugar.Values
                    .OrderByDescending(v => v.Usos)
                    .ThenByDescending(v => v.MasReciente.Momento)
                    .FirstOrDefault();
                if (mejor.Usos > 0)
                {
                    _deducido = new DatoSitio(mejor.MasReciente.Direccion, mejor.MasReciente.Latitud,
                        mejor.MasReciente.Longitud, "equipos", mejor.MasReciente.Momento);
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "No se pudo deducir la dirección del sitio de los reportes de los equipos");
                _deducido = null;
            }
            return _deducido;
        }
    }

    private static double Numero(JsonElement raiz, string clave)
    {
        if (!raiz.TryGetProperty(clave, out var v)) return double.NaN;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind != JsonValueKind.String) return double.NaN;
        return double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            ? n : double.NaN;
    }

    private static string Cadena(JsonElement raiz, string clave) =>
        raiz.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    /// <summary>Une «ciudad, estado, código postal» sin repetir lo que ya salió antes.</summary>
    private static string Une(params string[] partes)
    {
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var salida = new List<string>();
        foreach (var p in partes)
        {
            var s = (p ?? "").Trim();
            if (s.Length == 0 || !vistas.Add(s)) continue;
            salida.Add(s);
        }
        return string.Join(", ", salida);
    }
}
