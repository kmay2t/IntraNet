using System.Text.Json;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>
/// Lo que el nodo devolvió en el C-FIND de Study Root que la pantalla hace **después** de cada prueba:
/// eso es «lo que el PACS dice que ya tiene». `Soportada` es falsa cuando el destino no admitió la
/// consulta (rechazó la SOP Class, no respondió o tardó demasiado): en ese caso la pantalla lo dice tal
/// cual, en lugar de inventar un contenido.
/// </summary>
public sealed record PruebaVerificacion(bool Soportada, string Mensaje, IReadOnlyList<PruebaEstudioPacs> Estudios);

/// Un estudio que el destino reporta para el UID enviado. Es texto plano: llega del C-FIND tal cual lo
/// escribió el PACS, sin traducir ni completar.
public sealed record PruebaEstudioPacs(string EstudioUid, string Acceso, string Fecha, string Hora,
    string Modalidades, string Descripcion, int Instancias);

/// <summary>Los identificadores que quedaron escritos en la imagen que se envió. La verificación y el
/// registro preguntan y apuntan exactamente esto, no lo que el navegador creyó haber mandado.</summary>
public sealed record PruebaTags(string EstudioUid, string SerieUid, string InstanciaUid, string Acceso);

/// <summary>
/// Un envío de prueba técnica, tal y como quedó en el registro permanente de la intranet
/// (App_Data/dicom-pruebas.jsonl). Una línea por envío: quién, a qué nodo, qué tags salieron en la imagen,
/// qué respondió el destino y qué dijo después el PACS que tiene.
/// </summary>
public sealed record DicomPrueba(long Id, DateTime Momento, string Usuario, string Estado, string Mensaje,
    string Host, int Puerto, string AeRemoto, string AeLocal, PruebaTags Tags,
    IReadOnlyList<PruebaCampo> Campos, PruebaVerificacion Verificacion);

/// <summary>
/// Registro de las pruebas técnicas DICOM enviadas desde «Consulta lista de trabajo». A diferencia de la
/// bitácora de accesos guarda la estructura completa (los tags que salieron y lo que el PACS reportó), así
/// que vive en su propio archivo JSONL con la misma convención del resto de los almacenes de respaldo:
/// una línea por envío, escritura bajo bloqueo y **Registrar NUNCA lanza** —un fallo al apuntar no puede
/// romper un envío que ya ocurrió—.
/// </summary>
public interface IDicomPruebaStore
{
    void Registrar(DicomPrueba prueba);
    /// <param name="limite">Máximo de renglones, del más reciente hacia atrás.</param>
    IReadOnlyList<DicomPrueba> Listar(int limite = 50);
}

public sealed class JsonDicomPruebaStore(
    IWebHostEnvironment env, IConfiguration config, ILogger<JsonDicomPruebaStore> logger) : IDicomPruebaStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _lock = new();
    private readonly string _path = config["Dicom:PruebasFile"]
        ?? Path.Combine(env.ContentRootPath, "App_Data", "dicom-pruebas.jsonl");
    private long _secuencia;

    public void Registrar(DicomPrueba prueba)
    {
        try
        {
            lock (_lock)
            {
                var linea = new DicomPrueba(Interlocked.Increment(ref _secuencia), prueba.Momento,
                    Recortar(prueba.Usuario, 40), Recortar(prueba.Estado, 20), Recortar(prueba.Mensaje, 300),
                    Recortar(prueba.Host, 253), prueba.Puerto, Recortar(prueba.AeRemoto, 16),
                    Recortar(prueba.AeLocal, 16), prueba.Tags, prueba.Campos, prueba.Verificacion);
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, JsonSerializer.Serialize(linea, Json) + Environment.NewLine);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("Fallo al escribir la prueba DICOM en el registro: {Error}", e.Message);
        }
    }

    public IReadOnlyList<DicomPrueba> Listar(int limite = 50)
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return Array.Empty<DicomPrueba>();
                return File.ReadAllLines(_path)
                    .Where(l => l.Length > 0)
                    .Select(l =>
                    {
                        try { return JsonSerializer.Deserialize<DicomPrueba>(l, Json); }
                        catch { return null; }
                    })
                    .Where(p => p is not null)
                    .Select(p => p!)
                    .OrderByDescending(p => p.Momento).ThenByDescending(p => p.Id)
                    .Take(Math.Clamp(limite, 1, 200))
                    .ToList();
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("Fallo al leer el registro de pruebas DICOM: {Error}", e.Message);
            return Array.Empty<DicomPrueba>();
        }
    }

    private static string Recortar(string? valor, int max) =>
        string.IsNullOrEmpty(valor) ? "" : valor.Length <= max ? valor : valor[..max];
}
