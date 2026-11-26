using System.Net.Sockets;
using Meditechnosys.Intranet.Dicom;
using Meditechnosys.Intranet.Models;
using Meditechnosys.Intranet.Patologia;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// Sondeos REALES de salud para el panel SISTEMAS del inicio: base de datos (MySQL),
/// visor de estudios (alcance al nodo PACS) y consulta de reportes (carpeta del repositorio).
/// Cada sondeo es corto y con tope de ~2 s para no bloquear el panel ni el sondeo
/// periodico que hace el navegador cada minuto. Nunca lanzan: un fallo se reporta como "warn".
public static class Salud
{
    private const int TimeoutMs = 2000;

    /// Ping a MySQL con <c>SELECT 1</c>. Si no hay cadena de conexion o no responde, "warn".
    public static async Task<SystemStatus> BaseDeDatosAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return new SystemStatus("Base de datos", "warn", "Sin configurar");
        try
        {
            using var cn = new MySqlConnection(connectionString);
            using var cts = new CancellationTokenSource(TimeoutMs);
            await cn.OpenAsync(cts.Token);
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cts.Token);
            return new SystemStatus("Base de datos", "ok", "MySQL operativa");
        }
        catch
        {
            return new SystemStatus("Base de datos", "warn", "MySQL sin respuesta");
        }
    }

    /// Abre un socket TCP al primer nodo PACS: si acepta la conexion, el visor puede consultar.
    public static async Task<SystemStatus> VisorDicomAsync(IReadOnlyList<PacsNode>? nodos)
    {
        if (nodos is null || nodos.Count == 0)
            return new SystemStatus("Visor de estudios", "warn", "Sin nodos PACS");
        var nodo = nodos[0];
        try
        {
            using var tcp = new TcpClient();
            var conectar = tcp.ConnectAsync(nodo.Host, nodo.Port);
            if (await Task.WhenAny(conectar, Task.Delay(TimeoutMs)) != conectar)
                return new SystemStatus("Visor de estudios", "warn", "PACS sin respuesta");
            await conectar; // propaga la excepcion si el rechazo fue inmediato
            return new SystemStatus("Visor de estudios", "ok", "PACS conectado");
        }
        catch
        {
            return new SystemStatus("Visor de estudios", "warn", "PACS sin respuesta");
        }
    }

    /// La consulta de reportes vive sobre el repositorio en disco de Patologia:
    /// si la carpeta raiz existe, el modulo puede listar y abrir PDFs.
    public static SystemStatus ConsultaReportes(PatoDb? db)
    {
        try
        {
            if (db is null || string.IsNullOrEmpty(db.StorageDir))
                return new SystemStatus("Consulta de reportes", "warn", "Sin repositorio");
            return Directory.Exists(db.StorageDir)
                ? new SystemStatus("Consulta de reportes", "ok", "Repositorio activo")
                : new SystemStatus("Consulta de reportes", "warn", "Carpeta no encontrada");
        }
        catch
        {
            return new SystemStatus("Consulta de reportes", "warn", "Sin respuesta");
        }
    }

    /// Depuracion automatica de descargas DICOM. NO vuelve a medir el disco: usa los numeros que
    /// ya dio el ultimo repaso, porque el panel se refresa cada minuto y la carpeta puede tener
    /// miles de archivos. Asi el administrador ve cuanto ocupa y cuando se limpio, sin abrir bitacoras.
    public static SystemStatus DepuracionDescargas(LimpiadorDescargas? limpiador)
    {
        const string nombre = "Depuracion de descargas";
        try
        {
            if (limpiador is null)
                return new SystemStatus(nombre, "warn", "Sin servicio");
            if (!limpiador.Habilitado)
                return new SystemStatus(nombre, "warn", "Apagada (Local:DepurarDescargas)");
            if (!Directory.Exists(limpiador.Carpeta))
                return new SystemStatus(nombre, "warn", "Carpeta no encontrada");

            var u = limpiador.Ultima;
            if (u is null)
                return new SystemStatus(nombre, "ok", "Primer repaso en curso");

            var libre = u.BytesLiberados > 0 ? $" · {Espacio(u.BytesLiberados)} liberados" : "";
            var salteo = string.IsNullOrWhiteSpace(u.Salteo) ? "" : $" · {u.Salteo}";
            return new SystemStatus(nombre, "ok",
                $"{u.ArchivosRestantes} archivo(s), {Espacio(u.BytesRestantes)} · repasado {u.Momento:HH:mm}{libre}{salteo}");
        }
        catch
        {
            return new SystemStatus(nombre, "warn", "Sin respuesta");
        }
    }

    /// MB por debajo de 1 GB, GB arriba: lo que un administrador lee de un vistazo en el panel.
    private static string Espacio(long bytes) =>
        bytes >= 1073741824 ? $"{bytes / 1073741824.0:0.##} GB" : $"{bytes / 1048576.0:0.#} MB";
}
