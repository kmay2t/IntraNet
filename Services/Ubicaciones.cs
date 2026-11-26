using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Meditechnosys.Intranet.Models;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// <summary>
/// Una fila del mapa «rango de IP → ubicación física» que alimenta la columna Ubicación de la
/// bitácora de accesos. `Rango` admite `10.252.253.*`, `10.252.253.` (prefijo), una IP exacta o
/// CIDR (`10.252.253.0/24`); `Ubicacion` es texto libre, p. ej. «Radiología · Torre A, 2º piso».
/// </summary>
public record RangoUbicacion(string Rango, string Ubicacion);

/// Guardado del mapa de rangos. Igual que el resto de la intranet: MySQL y, si no hay base,
/// un archivo JSON en App_Data.
public interface IUbicacionStore
{
    IReadOnlyList<RangoUbicacion> Listar();
    /// <returns>204 guardada; 400 con el motivo si el rango no es válido.</returns>
    (int Status, string? Error) Guardar(string rango, string ubicacion);
    (int Status, string? Error) Borrar(string rango);
}

public class JsonUbicacionStore(IWebHostEnvironment env, IConfiguration config, ILogger<JsonUbicacionStore> logger)
    : IUbicacionStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Auth:UbicacionesFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "ubicaciones.json");

    private Dictionary<string, string> Leer()
    {
        if (!File.Exists(_path)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path), Json) ?? new(); }
        catch (Exception e) { logger.LogWarning("No se pudo leer ubicaciones.json: {Error}", e.Message); return new(); }
    }

    private void Escribir(Dictionary<string, string> datos)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(datos, Json));
    }

    public IReadOnlyList<RangoUbicacion> Listar()
    {
        lock (_lock) { return Leer().OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new RangoUbicacion(kv.Key, kv.Value)).ToList(); }
    }

    public (int, string?) Guardar(string rango, string ubicacion)
    {
        var (ok, error) = Ubicaciones.ValidarRango(rango);
        if (!ok) return (400, error);
        lock (_lock)
        {
            var datos = Leer();
            datos[rango.Trim()] = ubicacion.Trim();
            try { Escribir(datos); }
            catch (Exception e) { logger.LogWarning("No se pudo guardar ubicaciones.json: {Error}", e.Message); return (500, "No se pudo guardar."); }
            return (204, null);
        }
    }

    public (int, string?) Borrar(string rango)
    {
        lock (_lock)
        {
            var datos = Leer();
            if (!datos.Remove(rango.Trim())) return (404, "Ese rango no está en la lista.");
            try { Escribir(datos); }
            catch (Exception e) { logger.LogWarning("No se pudo guardar ubicaciones.json: {Error}", e.Message); return (500, "No se pudo guardar."); }
            return (204, null);
        }
    }
}

public class MySqlUbicacionStore(IConfiguration config) : IUbicacionStore
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
            CREATE TABLE IF NOT EXISTS `{db}`.`bitacora_ubicacion` (
              `rango`     VARCHAR(60)  NOT NULL PRIMARY KEY,
              `ubicacion` VARCHAR(160) NOT NULL DEFAULT ''
            ) ENGINE=InnoDB;
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<RangoUbicacion> Listar()
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT rango, ubicacion FROM bitacora_ubicacion ORDER BY rango";
            var lista = new List<RangoUbicacion>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) lista.Add(new RangoUbicacion(r.GetString(0), r.GetString(1)));
            return lista;
        }
        catch { return Array.Empty<RangoUbicacion>(); }
    }

    public (int, string?) Guardar(string rango, string ubicacion)
    {
        var (ok, error) = Ubicaciones.ValidarRango(rango);
        if (!ok) return (400, error);
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO bitacora_ubicacion (rango, ubicacion) VALUES (@r, @u)
                ON DUPLICATE KEY UPDATE ubicacion = @u
                """;
            cmd.Parameters.AddWithValue("@r", rango.Trim());
            cmd.Parameters.AddWithValue("@u", ubicacion.Trim());
            cmd.ExecuteNonQuery();
            return (204, null);
        }
        catch { return (500, "No se pudo guardar el rango."); }
    }

    public (int, string?) Borrar(string rango)
    {
        try
        {
            using var cn = Conexion();
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM bitacora_ubicacion WHERE rango = @r";
            cmd.Parameters.AddWithValue("@r", rango.Trim());
            return cmd.ExecuteNonQuery() > 0 ? (204, null) : (404, "Ese rango no está en la lista.");
        }
        catch { return (500, "No se pudo borrar el rango."); }
    }
}

/// <summary>
/// Reglas para leer el mapa de rangos: validar lo que escribe el administrador y emparejar una IP
/// contra la lista (gana la coincidencia más específica).
/// </summary>
public static class Ubicaciones
{
    /// <summary>Direcciones que son la propia máquina del servidor, no un puesto de trabajo.</summary>
    public const string Local = "Este equipo (el servidor)";

    public static bool EsLocal(string ip) =>
        ip is "127.0.0.1" or "::1" || ip.StartsWith("127.", StringComparison.Ordinal);

    /// <summary>El rango al que pertenece una IPv4: «10.252.252.68» → «10.252.252». Vacío si no es IPv4.</summary>
    public static string Prefijo(string? ip)
    {
        var partes = (ip ?? "").Split('.');
        return partes.Length == 4 && partes.All(p => p.Length > 0 && p.All(char.IsDigit))
            ? string.Join(".", partes.Take(3)) : "";
    }

    /// <summary>Lo que dice la columna cuando nadie puede saber dónde está el aparato (sin permiso).</summary>
    public const string SinConfirmar = "Sin confirmar";

    /// <summary>
    /// Deja apenas la zona de una dirección que salió de geolocalizar una IP: sin código postal y sin el
    /// dato fino (colonia o calle). La salida a Internet de todo el hospital es UN solo punto compartido,
    /// así que de ahí no se puede deducir una dirección postal; escribirla completa invita a creer que
    /// dice dónde está el aparato cuando apenas dice por dónde sale a Internet. Con la posición real del
    /// equipo (permiso del navegador) esto no se aplica: esa dirección sí es del puesto de trabajo.
    /// </summary>
    public static string SoloZona(string? texto)
    {
        var limpio = new List<string>();
        foreach (var trozo in (texto ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            // Se quitan los códigos postales (puros dígitos) que vienen pegados a la ciudad.
            var palabras = trozo.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.All(char.IsDigit) || p.Length is < 4 or > 5)
                .ToList();
            if (palabras.Count > 0) limpio.Add(string.Join(" ", palabras).Trim());
        }
        if (limpio.Count == 0) return "";
        // La primera parte suele ser la colonia; la zona de verdad son las dos últimas (alcaldía y ciudad).
        var desde = limpio.Count > 2 ? limpio.Count - 2 : 0;
        return string.Join(", ", limpio.Skip(desde));
    }

    public static (bool Ok, string? Error) ValidarRango(string? rango)
    {
        var r = (rango ?? "").Trim();
        if (r.Length == 0) return (false, "Escriba un rango, por ejemplo 10.252.253.*");
        if (r.Length > 60) return (false, "El rango es demasiado largo (máximo 60 caracteres).");

        // CIDR: 10.252.253.0/24
        var barra = r.IndexOf('/');
        if (barra > 0)
        {
            if (!IPAddress.TryParse(r[..barra], out var dir)) return (false, "La dirección antes de «/» no es válida.");
            if (!int.TryParse(r[(barra + 1)..], out var bits) || bits < 8 || bits > 32 ||
                dir.AddressFamily != AddressFamily.InterNetwork)
                return (false, "El número después de «/» debe estar entre 8 y 32 (IPv4).");
            return (true, null);
        }

        // Prefijo con comodín o IP exacta: 10.252.253.*, 10.252.253., 10.252.253.75
        var sinComodin = r.EndsWith("*") ? r[..^1] : r;
        if (sinComodin.Length == 0) return (false, "El rango debe empezar con números, por ejemplo 10.252.*");
        var partes = sinComodin.TrimEnd('.').Split('.');
        if (partes.Length < 1 || partes.Length > 4) return (false, "Escriba de 1 a 4 números separados por punto.");
        foreach (var p in partes)
            if (p.Length == 0 || !p.All(char.IsDigit) || int.TryParse(p, out var n) && (n < 0 || n > 255))
                return (false, $"«{p}» no es un número de dirección válido (0 a 255).");
        return (true, null);
    }

    /// <summary>¿Cae <paramref name="ip"/> dentro de <paramref name="rango"/>?</summary>
    public static bool Coincide(string ip, string rango)
    {
        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(rango)) return false;
        var r = rango.Trim();

        var barra = r.IndexOf('/');
        if (barra > 0)
        {
            if (!IPAddress.TryParse(r[..barra], out var red) || !int.TryParse(r[(barra + 1)..], out var bits)) return false;
            if (!IPAddress.TryParse(ip, out var dir)) return false;
            if (red.AddressFamily != dir.AddressFamily) return false;
            return MismoPrefijo(red.GetAddressBytes(), dir.GetAddressBytes(), bits);
        }

        var prefijo = r.EndsWith("*") ? r[..^1] : (r.EndsWith(".") ? r : r + ".");
        if (ip.StartsWith(prefijo, StringComparison.Ordinal)) return true;
        // «10.252.253.75» sin comodín ni punto también vale como IP exacta.
        return !r.EndsWith("*") && !r.EndsWith(".") && ip == r;
    }

    private static bool MismoPrefijo(byte[] red, byte[] dir, int bits)
    {
        for (var i = 0; i < 4 && bits > 0; i++)
        {
            var toma = Math.Min(8, bits);
            var mascara = (byte)(0xFF << (8 - toma) & 0xFF);
            if ((red[i] & mascara) != (dir[i] & mascara)) return false;
            bits -= toma;
        }
        return true;
    }

    /// <summary>
    /// Ubicación declarada para una IP: la coincidencia más larga (más específica) de la tabla.
    /// Vacío si ningún rango la cubre.
    /// </summary>
    public static string? UbicacionDe(string ip, IEnumerable<RangoUbicacion> rangos)
    {
        if (EsLocal(ip)) return Local;
        string? mejor = null;
        var larga = 0;
        foreach (var r in rangos)
        {
            if (r.Ubicacion.Trim().Length == 0) continue;
            if (!Coincide(ip, r.Rango)) continue;
            if (r.Rango.Length <= larga) continue;
            mejor = r.Ubicacion.Trim();
            larga = r.Rango.Length;
        }
        return mejor;
    }
}

/// <summary>
/// Convierte la IP de un renglón de la bitácora en «equipo» y «ubicación».
/// <list type="bullet">
/// <item><b>Equipo</b>: el nombre que da el DNS inverso de la red del hospital (10.252.253.75 →
/// RIS03). Se resuelve una sola vez por IP y el resultado se queda en memoria, con un tiempo límite
/// por si el DNS tarda, porque la búsqueda va en la consulta de la pantalla y nunca en el inicio de
/// sesión: registrar un evento no puede demorarse por una consulta de red.</item>
/// <item><b>Ubicación</b>: en este orden, y gana el primero que exista (<see cref="Decide"/>):
/// <list type="number">
/// <item>lo que alguien escribió para su rango de IP (<see cref="IUbicacionStore"/>);</item>
/// <item>la posición real que reportó el propio aparato con el permiso del navegador;</item>
/// <item>el punto real que aprendió su rango (/24) de otro equipo del mismo rango —así un puesto sin
/// permiso no sale «en Cuauhtémoc» solo porque su salida a Internet esté a 4 km, sino en el edificio
/// donde ya dijo estar su vecino;</item>
/// <item>la dirección del sitio cuando el administrador marcó el edificio en el mapa
/// (<see cref="LugarDelSitio"/>);</item>
/// <item>y si nada de eso hay, <see cref="Ubicaciones.SinConfirmar"/>. La zona que da la IP ya no se
/// pinta como ubicación: es la salida a Internet compartida por todo el hospital y suele caer en otra
/// alcaldía. Solo se menciona en la marca, para que se entienda por qué falta.</item>
/// </list>
/// Ninguna se guarda en el renglón: se deduce al consultar, así que dar un permiso —o corregir un
/// rango— acomoda también los registros viejos.</item>
/// </list>
/// </summary>
public class UbicacionResolver(IUbicacionStore store, ILugarStore lugares, LugarDelSitio sitio,
    ILogger<UbicacionResolver> logger)
{
    /// <summary>Presupuesto de tiempo para resolver los nombres que faltan en una consulta.</summary>
    private static readonly TimeSpan PresupuestoDns = TimeSpan.FromMilliseconds(900);

    /// <summary>Cuánto se guarda el nombre ya resuelto (o el «no tiene nombre»).</summary>
    private static readonly TimeSpan VidaCachéEquipo = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<string, (string Equipo, DateTime Caduca)> _equipos = new();
    private readonly object _gate = new();
    private IReadOnlyList<RangoUbicacion> _rangos = Array.Empty<RangoUbicacion>();
    private DateTime _caducanRangos = DateTime.MinValue;
    private IReadOnlyDictionary<string, LugarReportado> _lugares = new Dictionary<string, LugarReportado>();
    private DateTime _caducanLugares = DateTime.MinValue;
    private IReadOnlyDictionary<string, LugarReportado> _aprendidos = new Dictionary<string, LugarReportado>();

    private IReadOnlyList<RangoUbicacion> Rangos()
    {
        lock (_gate)
        {
            if (_caducanRangos > DateTime.UtcNow) return _rangos;
            try { _rangos = store.Listar(); }
            catch (Exception e) { logger.LogWarning("No se pudo leer la tabla de rangos: {Error}", e.Message); }
            _caducanRangos = DateTime.UtcNow.AddMinutes(1);
            return _rangos;
        }
    }

    /// <summary>Último lugar reportado por cada IP (se refresca como mucho cada minuto).</summary>
    private IReadOnlyDictionary<string, LugarReportado> Lugares()
    {
        lock (_gate)
        {
            if (_caducanLugares > DateTime.UtcNow) return _lugares;
            try { _lugares = lugares.UltimosPorIp(); }
            catch (Exception e) { logger.LogWarning("No se pudieron leer los lugares reportados: {Error}", e.Message); }
            _aprendidos = ArmaAprendidos(_lugares, PrefijosDeLaMaquina());
            _caducanLugares = DateTime.UtcNow.AddMinutes(1);
            return _lugares;
        }
    }

    /// <summary>El punto real que ya aprendió cada rango /24 (misma caducidad que los reportes).</summary>
    private IReadOnlyDictionary<string, LugarReportado> Aprendidos()
    {
        lock (_gate) { Lugares(); return _aprendidos; }
    }

    /// <summary>
    /// El punto más reciente, dado con permiso del navegador, para cada rango /24 de la red.
    ///
    /// Por qué existe: la geolocalización por IP mide la salida a Internet del proveedor —un solo punto
    /// compartido por todo el hospital— y aquí se ha comprobado que ese punto cae a más de 4 km del
    /// edificio, en otra alcaldía. En cambio, cuando UN puesto del rango 10.252.252.x dice su posición
    /// real, sus vecinos de rango están casi siempre en el mismo edificio: el rango del hospital es por
    /// zona física, así que heredar el punto es mucho más certero que la aproximación del proveedor.
    ///
    /// Los reportes que llegaron por 127.0.0.1 (alguien abrió la intranet desde el propio servidor, con
    /// «localhost», donde el navegador sí entrega el GPS) no cuelgan de ninguna IP de red, así que se
    /// reparten entre los rangos de las tarjetas del servidor: es la misma máquina, y el mismo sitio.
    /// </summary>
    public static IReadOnlyDictionary<string, LugarReportado> ArmaAprendidos(
        IReadOnlyDictionary<string, LugarReportado> lugares, IEnumerable<string> prefijosServidor)
    {
        var salida = new Dictionary<string, LugarReportado>(StringComparer.OrdinalIgnoreCase);
        var delServidor = prefijosServidor.Where(p => p.Length > 0).Distinct().ToArray();
        foreach (var l in lugares.Values)
        {
            if (!EsPosicionReal(l) || l.Direccion.Trim().Length == 0) continue;
            foreach (var prefijo in Ubicaciones.EsLocal(l.Ip) ? delServidor : new[] { Ubicaciones.Prefijo(l.Ip) })
            {
                if (prefijo.Length == 0) continue;
                if (salida.TryGetValue(prefijo, out var previo) && previo.Momento >= l.Momento) continue;
                salida[prefijo] = l;
            }
        }
        return salida;
    }

    /// <summary>¿Este reporte llegó por IP (la aproximación del proveedor), no por el navegador?</summary>
    private static bool EsPorIp(LugarReportado l) =>
        !string.Equals(l.Fuente, "navegador", StringComparison.OrdinalIgnoreCase);

    /// <summary>¿Este reporte trae una posición de verdad (permiso del navegador y coordenadas válidas)?</summary>
    private static bool EsPosicionReal(LugarReportado l) =>
        !EsPorIp(l) && GeocodificadorLugar.EsCoordenada(l.Latitud, l.Longitud);

    /// <summary>Los rangos /24 de las tarjetas de red del propio servidor (sin 127.* ni IPv6).</summary>
    private static IReadOnlyList<string> PrefijosDeLaMaquina()
    {
        var salida = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var dir in nic.GetIPProperties().UnicastAddresses)
                {
                    if (dir.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var prefijo = Ubicaciones.Prefijo(dir.Address.ToString());
                    if (prefijo.Length > 0 && !prefijo.StartsWith("127.", StringComparison.Ordinal) &&
                        !salida.Contains(prefijo)) salida.Add(prefijo);
                }
            }
        }
        catch { /* sin tarjetas legibles no hay rangos que heredar: los puestos salen «Sin confirmar» */ }
        return salida;
    }

    /// <summary>Fuerza la recarga de la tabla (después de que el administrador edita un rango).</summary>
    public void InvalidaRangos()
    {
        lock (_gate) { _caducanRangos = DateTime.MinValue; _caducanLugares = DateTime.MinValue; }
    }

    /// <summary>
    /// Ubicación de una IP: lo escrito para su rango; si no, su posición real (permiso del navegador);
    /// si no, el punto real que aprendió su rango de un vecino; si no, el edificio calibrado; y si nada
    /// de eso hay, «Sin confirmar». Ver <see cref="Decide"/> para el orden completo.
    /// </summary>
    public string UbicacionDe(string ip) => Decidir(ip).Texto;

    /// <summary>Último lugar geográfico que reportó esa IP, o null si nunca ha reportado uno.</summary>
    public LugarReportado? LugarDe(string ip) =>
        ip is not null && Lugares().TryGetValue(ip, out var l) ? l : null;

    /// <summary>La dirección geográfica del sitio, obtenida sola (sin bloquear nunca la pantalla).</summary>
    public DatoSitio? Sitio() => sitio.Actual();

    /// <summary>La dirección del sitio, o cadena vacía si todavía no se sabe.</summary>
    public string DireccionDelSitio() => sitio.Direccion;

    /// <summary>El punto que marcó el administrador en el mapa; null si la intranet sigue en automático.</summary>
    public SitioCalibrado? Calibracion() => sitio.Calibrado();

    /// <summary>¿El sitio ya tiene su edificio marcado en el mapa? Es la última ubicación con nombre.</summary>
    public bool SitioCalibrado() => sitio.Calibrado() is not null;

    /// <summary>Se olvida de la calibración en memoria: el cambio se nota en la siguiente consulta.</summary>
    public void InvalidaSitio() => sitio.InvalidaCalibracion();

    /// <summary>
    /// Todo lo que se dice de una IP de golpe: el texto de la columna, de dónde salió, su margen en
    /// metros y el punto del mapa que lo respalda. La pantalla, la API y el Excel preguntan por este
    /// mismo resultado, así que nunca se contradicen entre sí.
    /// </summary>
    private Decision Decidir(string? ip) => Decide(ip, Rangos(), Lugares(), Aprendidos(),
        DireccionDelSitio(), SitioCalibrado(), CoordenadasDelSitio());

    /// <summary>La coordenada del sitio, pero solo cuando el edificio ya fue marcado en el mapa.</summary>
    private string CoordenadasDelSitio()
    {
        var fijado = sitio.Calibrado();
        return fijado is not null && GeocodificadorLugar.EsCoordenada(fijado.Latitud, fijado.Longitud)
            ? PuntosDeMapa(fijado.Latitud, fijado.Longitud) : "";
    }

    /// <summary>Lo que se muestra de una IP: texto, origen, metros y punto del mapa.</summary>
    private record Decision(string Texto, string Marca, int Metros, string Puntos);

    private static Decision Decide(string? ip, IReadOnlyList<RangoUbicacion> rangos,
        IReadOnlyDictionary<string, LugarReportado> lugares,
        IReadOnlyDictionary<string, LugarReportado> aprendidos, string direccionSitio,
        bool sitioCalibrado, string coordenadasSitio)
    {
        // El renglón que apuntó el propio servidor no viene de ningún aparato de la red.
        if (Ubicaciones.EsLocal(ip ?? "")) return new(Ubicaciones.Local, "", 0, "");

        var escrita = Ubicaciones.UbicacionDe(ip ?? "", rangos);
        if (!string.IsNullOrEmpty(escrita)) return new(escrita, "escrita para su rango de IP", 0, "");

        LugarReportado? propio = ip is not null && lugares.TryGetValue(ip, out var l) ? l : null;

        // 1) La posición que dio el propio aparato con permiso del navegador: es su GPS o su wifi, así
        //    que vale más que cualquier deducción.
        if (propio is not null && EsPosicionReal(propio))
            return new(propio.Direccion, "posición real del equipo", Math.Max(0, propio.PrecisionMetros),
                PuntosDeMapa(propio.Latitud, propio.Longitud));

        // 2) El punto real de un vecino del mismo rango /24. Aquí es donde antes se ponía la zona que
        //    da la IP, y por eso salía «Cuauhtémoc» un puesto que está en Vallejo: la geolocalización
        //    por IP mide la salida a Internet del proveedor —un punto solo, compartido por todo el
        //    hospital— y ese punto comprobado está a más de 4 km del edificio, en otra alcaldía. Heredar
        //    el punto de quien sí lo dio es más certero, y lo dice la marca.
        var prefijo = Ubicaciones.Prefijo(ip ?? "");
        if (prefijo.Length > 0 && aprendidos.TryGetValue(prefijo, out var aprendido))
            return new(aprendido.Direccion, "punto aprendido del mismo rango de IP",
                Math.Max(0, aprendido.PrecisionMetros), PuntosDeMapa(aprendido.Latitud, aprendido.Longitud));

        // 3) El edificio marcado en el mapa, si alguien lo marcó.
        if (sitioCalibrado && direccionSitio.Trim().Length > 0)
            return new(direccionSitio, "dirección del sitio", 0, coordenadasSitio);

        // Un renglón sin IP (lo apuntó el propio sistema) no tiene de dónde deducir un lugar.
        if (string.IsNullOrWhiteSpace(ip)) return new("", "", 0, "");

        // 4) Nada fiable: se dice «Sin confirmar» y la marca explica por qué (falta el permiso) y por
        //    dónde anda su salida a Internet, que es lo único que la IP sabe.
        var salePorIp = propio is not null && !EsPosicionReal(propio) && EsPorIp(propio);
        var zona = salePorIp ? Ubicaciones.SoloZona(propio!.Direccion) : "";
        var marca = zona.Length > 0
            ? $"sin permiso del navegador (su salida a Internet sale por {zona})"
            : propio is not null ? "el punto que reportó el equipo no trae coordenadas" : "sin permiso del navegador";
        return new(Ubicaciones.SinConfirmar, marca, 0, "");
    }

    /// <summary>
    /// De dónde salió la ubicación que va a mostrar <see cref="UbicacionDe(string)"/>, para que la
    /// pantalla y el Excel digan si el punto es del propio aparato, de un vecino de rango, o si todavía
    /// no se sabe. Es el mismo orden de decisión, así que nunca se contradicen.
    /// </summary>
    public (string Marca, int Metros) MarcaDe(string ip)
    {
        var d = Decidir(ip);
        return (d.Marca, d.Metros);
    }

    /// <summary>
    /// El punto del mapa que respalda la ubicación de esa IP, escrito «19.432600, -99.127600», siguiendo
    /// el mismo orden que <see cref="UbicacionDe"/>: el punto que reportó el aparato, el que aprendió su
    /// rango, o el que calibró el administrador.
    ///
    /// Queda vacío cuando la ubicación la escribió alguien para un rango de IP —un texto a mano no tiene
    /// coordenadas— o cuando todavía no se sabe dónde está el aparato. Es la columna que acompaña a la
    /// dirección en el Excel: ante Recursos Humanos, el punto exacto pesa tanto como la calle.
    /// </summary>
    public string CoordenadasDe(string ip) => Decidir(ip).Puntos;

    /// <summary>Coordenadas con seis decimales (~10 cm) y punto como separador, para Excel y para MySQL.</summary>
    private static string PuntosDeMapa(double latitud, double longitud) =>
        latitud.ToString("0.000000", CultureInfo.InvariantCulture) + ", " +
        longitud.ToString("0.000000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Nombres de equipo de varias IPs a la vez. Las que ya están en caché salen al instante; las
    /// nuevas se resuelven en paralelo y las que no terminen a tiempo quedan vacías en esta consulta
    /// (se reintentan en la siguiente).
    /// </summary>
    public async Task<Dictionary<string, string>> EquiposAsync(IEnumerable<string> ips, CancellationToken ct = default)
    {
        var salida = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pendientes = new List<string>();
        var ahora = DateTime.UtcNow;

        foreach (var ip in ips.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Ubicaciones.EsLocal(ip)) { salida[ip] = ""; continue; }
            if (_equipos.TryGetValue(ip, out var guardado) && guardado.Caduca > ahora)
            {
                if (guardado.Equipo.Length > 0) salida[ip] = guardado.Equipo;
                continue;
            }
            pendientes.Add(ip);
        }
        if (pendientes.Count == 0) return salida;

        var tareas = pendientes.Select(async ip =>
        {
            try
            {
                var entradas = await Dns.GetHostEntryAsync(ip).ConfigureAwait(false);
                var nombre = entradas.HostName ?? "";
                // Un PTR que solo devuelve números no aporta nada.
                if (nombre == ip || nombre.All(c => char.IsDigit(c) || c == '.')) nombre = "";
                return (ip, nombre.Split('.')[0]);
            }
            catch (SocketException) { return (ip, ""); }        // la red no tiene ese nombre registrado
            catch (Exception e) { logger.LogDebug(e, "DNS inverso de {Ip} falló", ip); return (ip, ""); }
        }).ToList();

        var ganadora = await Task.WhenAny(Task.WhenAll(tareas), Task.Delay(PresupuestoDns, CancellationToken.None));
        foreach (var t in tareas)
        {
            if (!t.IsCompleted) continue;                        // sin resultado todavía: se reintenta luego
            var (ip, nombre) = t.IsCompletedSuccessfully ? t.Result : (pendientes[tareas.IndexOf(t)], "");
            _equipos[ip] = (nombre, DateTime.UtcNow.Add(nombre.Length > 0 ? VidaCachéEquipo : TimeSpan.FromMinutes(10)));
            if (nombre.Length > 0) salida[ip] = nombre;
        }
        return salida;
    }

    /// <summary>
    /// Agrega equipo y ubicación a renglones ya leídos de la bitácora. Deja como viene la lista si
    /// algo falla: la pantalla debe mostrar los movimientos aunque la red no responda.
    /// </summary>
    public async Task<IReadOnlyList<AuditEntry>> EnriquecerAsync(IReadOnlyList<AuditEntry> entradas, CancellationToken ct = default)
    {
        try
        {
            var equipos = await EquiposAsync(entradas.Select(e => e.Ip), ct);
            var rangos = Rangos();
            var lugares = Lugares();
            var aprendidos = Aprendidos();
            // La dirección del sitio es la misma para todos los renglones: se lee una vez, no una por fila.
            var direccionSitio = DireccionDelSitio();
            var calibrado = SitioCalibrado();
            var puntosSitio = CoordenadasDelSitio();
            return entradas.Select(e =>
            {
                var d = Decide(e.Ip, rangos, lugares, aprendidos, direccionSitio, calibrado, puntosSitio);
                return e with
                {
                    Equipo = e.Ip.Length > 0 && equipos.TryGetValue(e.Ip, out var nom) ? nom : "",
                    Ubicacion = d.Texto,
                    FuenteUbicacion = d.Marca,
                    PrecisionMetros = d.Metros
                };
            }).ToList();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "No se pudo calcular equipo/ubicación en la bitácora");
            return entradas;
        }
    }
}
