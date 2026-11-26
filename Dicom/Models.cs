namespace Meditechnosys.Intranet.Dicom;

/// <summary>Nodo PACS remoto contra el que se puede consultar/descargar.</summary>
public class PacsNode
{
    /// <summary>Nombre corto para mostrar en la UI (por defecto = AeTitle).</summary>
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string AeTitle { get; set; } = "";
}

public class StudyInfo
{
    public string PatientName { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string StudyDate { get; set; } = "";
    public string StudyDescription { get; set; } = "";
    public string ModalitiesInStudy { get; set; } = "";
    public string NumberOfInstances { get; set; } = "";
    public string StudyInstanceUid { get; set; } = "";
    /// <summary>AE Title del PACS de origen donde se encontró el estudio (necesario para descargarlo).</summary>
    public string SourceAeTitle { get; set; } = "";
    /// <summary>Nombre amigable del nodo de origen.</summary>
    public string SourceName { get; set; } = "";
}

public class SearchRequest
{
    public string? PatientId { get; set; }
    public string? PatientName { get; set; }
    public string? StudyDate { get; set; }
    public string? AccessionNumber { get; set; }
    public string? ModalitiesInStudy { get; set; }
    public string? StudyDescription { get; set; }

    /// <summary>
    /// AE Titles (o nombres de nodo) contra los que consultar. Si viene vacío/null,
    /// se consultan TODOS los nodos configurados en PacsNodes.
    /// </summary>
    public List<string>? AeTitles { get; set; }
}

/// <summary>Estudio a descargar, identificado por su UID y el nodo PACS de origen.</summary>
public class DownloadItem
{
    public string StudyInstanceUid { get; set; } = "";
    /// <summary>AE Title del PACS de origen. Si se omite, se usa el primer nodo configurado.</summary>
    public string? AeTitle { get; set; }
    /// <summary>Número esperado de instancias (de C-FIND) para estimar progreso.</summary>
    public int? ExpectedInstances { get; set; }

    // Datos de identificación que ya trae la pantalla al buscar. Sirven para que los avisos de la
    // verificación digan de qué paciente se trata en vez de solo un UID ilegible.
    public string? PatientName { get; set; }
    public string? StudyDate { get; set; }
    public string? SourceName { get; set; }
}

public class DownloadRequest
{
    /// <summary>Forma nueva: cada estudio con su nodo de origen (soporta múltiples AE Titles a la vez).</summary>
    public List<DownloadItem>? Items { get; set; }

    /// <summary>Compatibilidad hacia atrás: lista simple de UIDs, todos contra el primer nodo configurado.</summary>
    public List<string>? StudyInstanceUids { get; set; }
}

/// <summary>
/// Una de las partes en que se divide el ZIP de una descarga. Antes, si pedías más de
/// <c>ZipMaxStudies</c> estudios, simplemente no se generaba ZIP; ahora los archivos se empaquetan
/// en varios ZIP numerados que el navegador baja uno tras otro.
/// </summary>
public class ZipPart
{
    /// <summary>1 para el primero. 0 si la descarga quedó en un solo ZIP (compatibilidad).</summary>
    public int Number { get; set; }
    public int Total { get; set; }
    public string FileName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long Bytes { get; set; }
    /// <summary>Estudios y archivos DICOM que van dentro de esta parte.</summary>
    public int Studies { get; set; }
    public int Files { get; set; }
}

/// <summary>
/// Estudio que NO llegó completo: el PACS dice que tiene <see cref="Expected"/> imágenes y en el
/// servidor se guardaron <see cref="Received"/>. Sale de la verificación posterior a la descarga.
/// </summary>
public class IncompleteStudy
{
    public string StudyInstanceUid { get; set; } = "";
    /// <summary>0 = el PACS no informó el conteo y tampoco llegó ninguna imagen.</summary>
    public int Expected { get; set; }
    public int Received { get; set; }
    /// <summary>Nombre y fecha del estudio, para que la persona sepa de qué paciente se trata.</summary>
    public string PatientName { get; set; } = "";
    public string StudyDate { get; set; } = "";
    public string SourceName { get; set; } = "";
    public int Faltantes => Math.Max(0, Expected - Received);
}

public class DownloadResult
{
    public int StudiesRequested { get; set; }
    public int StudiesCompleted { get; set; }
    public int ImagesSaved { get; set; }
    public string OutputPath { get; set; } = "";
    public List<string> Messages { get; set; } = new();
    /// <summary>Nombre del ZIP generado para descargar en el navegador (relativo a /api/files/)</summary>
    public string? ZipFileName { get; set; }
    /// <summary>URL para descargar el ZIP en la PC del usuario</summary>
    public string? ZipDownloadUrl { get; set; }

    // ---------- Verificación de integridad ----------

    /// <summary>true si la descarga pasó por la verificación (conteo imagen por imagen).</summary>
    public bool Verified { get; set; }
    /// <summary>Estudios que quedaron incompletos incluso después de los reintentos.</summary>
    public List<IncompleteStudy> IncompleteStudies { get; set; } = new();
    /// <summary>Imágenes que faltaban en la primera comprobación.</summary>
    public int MissingInstancesInitially { get; set; }
    /// <summary>Imágenes que siguen faltando al final.</summary>
    public int MissingInstances { get; set; }
    /// <summary>Estudios que se volvieron a pedir al PACS en las rondas de reintento.</summary>
    public int RetriedStudies { get; set; }
    /// <summary>Rondas de verificación que se llegaron a ejecutar (0 = no hizo falta reintentar).</summary>
    public int VerificationRounds { get; set; }

    /// <summary>Todas las partes del ZIP, en orden. La primera coincide con <see cref="ZipFileName"/>.</summary>
    public List<ZipPart> ZipParts { get; set; } = new();
}

/// <summary>Estado de un trabajo de descarga en curso (para barra de progreso).</summary>
public class DownloadProgress
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "pending"; // pending | running | completed | failed | cancelled
    /// <summary>Resumen legible del trabajo (paciente/estudios/nodo) para la lista de descargas.</summary>
    public string Label { get; set; } = "";
    public int StudiesRequested { get; set; }
    public int StudiesCompleted { get; set; }
    public int ImagesSaved { get; set; }
    public int ImagesExpected { get; set; }
    /// <summary>0–100. Si ImagesExpected=0, se estima por estudios.</summary>
    public int Percent { get; set; }
    public string Phase { get; set; } = ""; // retrieving | verifying | retrying | zipping | done
    public List<string> Messages { get; set; } = new();
    public string? ZipFileName { get; set; }
    public string? ZipDownloadUrl { get; set; }
    public string? Error { get; set; }
    public string OutputPath { get; set; } = "";

    // ---------- Verificación de integridad (se llena al terminar) ----------

    public bool Verified { get; set; }
    public List<IncompleteStudy> IncompleteStudies { get; set; } = new();
    public int MissingInstances { get; set; }
    public int RetriedStudies { get; set; }
    /// <summary>Partes de ZIP que se pueden bajar de este trabajo (1 o más).</summary>
    public List<ZipPart> ZipParts { get; set; } = new();
}

public class DownloadJobResponse
{
    public string JobId { get; set; } = "";
    public string StatusUrl { get; set; } = "";
    /// <summary>false si quedó en espera porque ya hay otras descargas trabajando.</summary>
    public bool StartedNow { get; set; } = true;
}

public class ApiStatus
{
    public string Status { get; set; } = "ok";
    public string LocalAeTitle { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string RetrieveMode { get; set; } = "C-MOVE";
    /// <summary>Nodos PACS configurados (para poblar el selector de AE Titles en la UI).</summary>
    public List<PacsNode> Nodes { get; set; } = new();

    // ---------- Cómo trabaja el apartado de descargas ----------

    /// <summary>Descargas que se ejecutan a la vez; las demás quedan en cola.</summary>
    public int MaxSimultaneous { get; set; }
    /// <summary>Si está encendida la verificación de integridad posterior a cada descarga.</summary>
    public bool Verify { get; set; }
    /// <summary>Rondas máximas de re-intento cuando falta alguna imagen.</summary>
    public int MaxRetries { get; set; }
    /// <summary>Estudios máximos por parte de ZIP.</summary>
    public int ZipMaxStudiesPerPart { get; set; }
    /// <summary>MB máximos por parte de ZIP.</summary>
    public int ZipMaxMBPerPart { get; set; }
    /// <summary>Trabajos de descarga que hay ahora mismo vivos (en curso o en cola).</summary>
    public int ActiveJobs { get; set; }
}
