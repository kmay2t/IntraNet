using System.IO;
using System.Linq;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>
/// Resultado de un repaso de la carpeta de descargas. Sirve para el log y para que la intranet
/// pueda informar cuánto espacio hay y cuándo fue la última depuración.
/// </summary>
public sealed class Depuracion
{
    public DateTime Momento { get; init; } = DateTime.Now;
    public string Motivo { get; init; } = "";
    public int ArchivosBorrados { get; init; }
    public long BytesLiberados { get; init; }
    public int ArchivosRestantes { get; init; }
    public long BytesRestantes { get; init; }
    public int CarpetasPodadas { get; init; }
    /// <summary>Por qué no se borró nada (descarga en curso, carpeta vacía…). Null si sí trabajó.</summary>
    public string? Salteo { get; init; }

    public static double MB(long bytes) => bytes / 1048576.0;

    public string Resumen => Salteo is not null
        ? $"sin cambios ({Salteo})"
        : $"{ArchivosBorrados} archivos · {MB(BytesLiberados):F1} MB liberados · " +
          $"quedan {ArchivosRestantes} archivos · {MB(BytesRestantes):F1} MB";
}

/// <summary>
/// La reglas de depuración, separadas del temporizador para poder probarlas sobre una carpeta
/// cualquiera sin arrancar el servicio DICOM.
///
/// Qué borra y por qué:
/// 1. los `.dcm` (y los `.tmp` que deja una transferencia cortada) **ya mayores** de `conservar`;
/// 2. los ZIP de `_zips` que pasaron de `conservarZips` (los ZIP se conservan más porque son lo que
///    la gente baja a su PC, y crearlos otra vez costaría otra consulta al PACS);
/// 3. las carpetas de paciente/estudio que quedaron vacías;
/// 4. y si aun así la carpeta pasa de `maxBytes`, lo más viejo primero —esa es la garantía real de
///    que una descarga masiva no llena el disco entre repasos. Con `maxBytes` en 0 no se usa esta
///    regla: la limpieza es solamente por antigüedad, nunca borra estudios recientes por tamaño.
///
/// Nunca toca nada si hay una transferencia en curso (<paramref name="hayTrabajoActivo"/>: descargas
/// o reenvíos, porque estos últimos escriben sus .dcm en la misma carpeta) y nunca borra
/// un archivo modificado en el último <see cref="MargenProteccion"/>: una transferencia puede estar
/// escribiendo justo ahora aunque el registro de trabajos todavía no lo refleje.
/// </summary>
internal static class DepuradorDescargas
{
    internal static readonly TimeSpan MargenProteccion = TimeSpan.FromMinutes(2);
    internal const string CarpetaZips = "_zips";

    public static Depuracion Ejecutar(
        string raiz,
        TimeSpan conservar,
        TimeSpan conservarZips,
        long maxBytes,
        bool hayTrabajoActivo,
        string motivo)
    {
        if (!Directory.Exists(raiz))
            return new Depuracion { Motivo = motivo, Salteo = "la carpeta de descargas no existe" };
        if (hayTrabajoActivo)
            return new Depuracion { Motivo = motivo, Salteo = "hay una descarga o un reenvío en curso" };

        var zips = Path.Combine(raiz, CarpetaZips);
        var ahora = DateTime.UtcNow;
        var borrados = new List<(string Ruta, long Bytes)>();

        // 1 y 2) lo que ya cumplió su tiempo, separando el árbol DICOM de la carpeta de ZIP.
        foreach (var archivo in CaminarArchivos(raiz))
        {
            var info = IntentarInfo(archivo);
            if (info is null) continue;
            // Margen de protección: lo que se escribió hace menos de dos minutos se queda, aunque
            // la antigüedad configurada sea cero. Así una transferencia que arrancó en este instante
            // no pierde sus primeras imágenes si el repaso cae justo encima.
            if (ahora - info.LastWriteTimeUtc < MargenProteccion) continue;
            var esZip = EsDentroDe(archivo, zips);
            var limite = esZip ? conservarZips : conservar;
            if (limite < TimeSpan.Zero) continue;                    // negativo = "nunca" para ese tipo de archivo
            if (ahora - info.LastWriteTimeUtc >= limite) borrados.Add((archivo, info.Length));
        }

        Borrar(borrados, out var liberados, out var fracasados);

        // 3) carpetas vacías, de adentro hacia afuera (un estudio sin archivos deja la suya y a veces la del paciente).
        var podadas = PodarVacías(raiz, zips);

        // 4) tope de espacio opcional: solo con el árbol DICOM; los ZIP se dejan a su propia regla.
        Medir(raiz, out var archivosRestantes, out var bytesRestantes);
        if (maxBytes > 0 && bytesRestantes > maxBytes)
        {
            var viejos = CaminarArchivos(raiz)
                .Where(f => !EsDentroDe(f, zips))
                .Select(f => (Ruta: f, Info: IntentarInfo(f)))
                .Where(x => x.Info is not null && ahora - x.Info!.LastWriteTimeUtc > MargenProteccion)
                .OrderBy(x => x.Info!.LastWriteTimeUtc)
                .ToList();

            foreach (var (ruta, info) in viejos)
            {
                if (bytesRestantes <= maxBytes) break;
                if (info is null) continue;
                try
                {
                    File.Delete(ruta);
                    borrados.Add((ruta, info.Length));
                    liberados += info.Length;
                    bytesRestantes -= info.Length;
                    archivosRestantes--;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* intacto hasta el próximo repaso */ }
            }
            podadas += PodarVacías(raiz, zips);
            Medir(raiz, out archivosRestantes, out bytesRestantes);
        }

        return new Depuracion
        {
            Motivo = motivo,
            ArchivosBorrados = borrados.Count,
            BytesLiberados = liberados,
            CarpetasPodadas = podadas,
            ArchivosRestantes = archivosRestantes,
            BytesRestantes = bytesRestantes,
            Salteo = fracasados > 0 ? $"{fracasados} archivo(s) estaban bloqueados" : null,
        };
    }

    /// <summary>Archivos bajo <paramref name="raiz"/>, sin seguir ningún enlace. Tolerante a permisos.</summary>
    private static IEnumerable<string> CaminarArchivos(string raiz)
    {
        var pila = new Stack<string>();
        pila.Push(raiz);
        while (pila.Count > 0)
        {
            var dir = pila.Pop();
            string[] subs = Array.Empty<string>(), archivos = Array.Empty<string>();
            try { subs = Directory.GetDirectories(dir); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            try { archivos = Directory.GetFiles(dir); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            foreach (var s in subs) pila.Push(s);
            foreach (var a in archivos) yield return a;
        }
    }

    private static FileInfo? IntentarInfo(string archivo)
    {
        try { return new FileInfo(archivo); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool EsDentroDe(string archivo, string carpeta)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(archivo));
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpeta));
        return a.StartsWith(c + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void Borrar(List<(string Ruta, long Bytes)> lista, out long liberados, out int fracasados)
    {
        liberados = 0;
        fracasados = 0;
        foreach (var (ruta, bytes) in lista)
        {
            try
            {
                File.Delete(ruta);
                liberados += bytes;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                fracasados++;   // en uso (un visor abierto, por ejemplo): se reintenta en el próximo repaso
            }
        }
    }

    /// <summary>Quita las carpetas que quedaron sin archivos ni subcarpetas. Conserva la raíz y _zips.</summary>
    private static int PodarVacías(string raiz, string zips)
    {
        var podadas = 0;
        var carpetas = new List<string>();
        var pila = new Stack<string>();
        pila.Push(raiz);
        while (pila.Count > 0)
        {
            var dir = pila.Pop();
            try { foreach (var s in Directory.GetDirectories(dir)) { carpetas.Add(s); pila.Push(s); } }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        // De más profunda a más superficial: así un estudio vacío se quita antes que la carpeta del paciente.
        foreach (var c in carpetas.OrderByDescending(x => x.Length))
        {
            var ruta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(c));
            if (string.Equals(ruta, Path.TrimEndingDirectorySeparator(Path.GetFullPath(raiz)), StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(ruta, Path.TrimEndingDirectorySeparator(Path.GetFullPath(zips)), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (Directory.EnumerateFileSystemEntries(c).Any()) continue;
                Directory.Delete(c);
                podadas++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return podadas;
    }

    private static void Medir(string raiz, out int archivos, out long bytes)
    {
        archivos = 0; bytes = 0;
        foreach (var f in CaminarArchivos(raiz))
        {
            var info = IntentarInfo(f);
            if (info is null) continue;
            archivos++; bytes += info.Length;
        }
    }
}
