using Microsoft.Extensions.Options;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>
/// Vigilante interno que depura la carpeta de descargas DICOM sin que nadie tenga que entrar a
/// borrar archivos a mano. Corre cada <c>Local:DepurarCadaMinutos</c> (60 por defecto) y aplica las
/// reglas de <see cref="DepuradorDescargas"/>.
///
/// Por qué hace falta: cada estudio descargado deja sus `.dcm` en el servidor y antes no se
/// borraban nunca, así que una sola descarga grande (1 GB en la práctica) se quedaba ocupando
/// espacio para siempre y al final saturaba el disco de la PC de la intranet.
/// </summary>
public class LimpiadorDescargas : BackgroundService
{
    private readonly DicomQrService _dicom;
    private readonly DownloadJobService _trabajos;
    private readonly ReenvioJobService _reenvios;
    private readonly LocalOptions _local;
    private readonly ILogger<LimpiadorDescargas> _log;

    public LimpiadorDescargas(DicomQrService dicom, DownloadJobService trabajos,
        ReenvioJobService reenvios, IOptions<LocalOptions> local, ILogger<LimpiadorDescargas> log)
    {
        _dicom = dicom; _trabajos = trabajos; _reenvios = reenvios; _local = local.Value; _log = log;
        if (_local.DepurarCadaMinutos < 1) _local.DepurarCadaMinutos = 1;
        if (_local.ConservarHoras < 0) _local.ConservarHoras = 0;
        if (_local.MaxMB < 0) _local.MaxMB = 0;
        if (_local.EspacioLibreMinimoGB < 0) _local.EspacioLibreMinimoGB = 0;
    }

    /// <summary>Último repaso, para que la intranet pueda mostrarlo en el estado del sistema.</summary>
    public Depuracion? Ultima { get; private set; }

    public string Carpeta => _dicom.OutputRoot;

    /// <summary>Si la depuración está encendida (<c>Local:DepurarDescargas</c>).</summary>
    public bool Habilitado => _local.DepurarDescargas;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_local.DepurarDescargas)
        {
            _log.LogInformation("Depuración de descargas DICOM desactivada (Local:DepurarDescargas=false).");
            return;
        }

        _log.LogInformation(
            "Depuración de descargas DICOM cada {Min} min: conserva {H} h de archivos, tope {MB} MB, ZIP {ZH} h, " +
            "solo con menos de {Libre} GB libres.",
            _local.DepurarCadaMinutos, _local.ConservarHoras, _local.MaxMB, _local.ZipRetentionHours,
            _local.EspacioLibreMinimoGB);

        // Primer repaso poco después de arrancar: si la PC se reinició a mitad de una descarga,
        // eso es lo que deja basura que nadie va a volver a tocar.
        await Esperar(TimeSpan.FromSeconds(20), ct);

        while (!ct.IsCancellationRequested)
        {
            try { Repasar("repaso programado"); }
            catch (Exception e) { _log.LogWarning(e, "La depuración de descargas DICOM falló; se reintenta en el próximo repaso."); }
            await Esperar(TimeSpan.FromMinutes(_local.DepurarCadaMinutos), ct);
        }
    }

    /// <summary>Da un repaso ahora mismo. Lo llama el temporizador y también el endpoint de estado.</summary>
    public Depuracion Repasar(string motivo)
    {
        // Condición de espacio: con 50 GB o más libres no hace falta borrar descargas antiguas.
        // Si no se puede leer el disco, se prefiere no borrar nada hasta el próximo repaso.
        if (_local.EspacioLibreMinimoGB > 0)
        {
            try
            {
                var raizDisco = Path.GetPathRoot(Path.GetFullPath(_dicom.OutputRoot));
                var unidad = new DriveInfo(raizDisco!);
                var libreGB = unidad.AvailableFreeSpace / 1073741824.0;
                if (libreGB >= _local.EspacioLibreMinimoGB)
                    return Guardar(new Depuracion
                    {
                        Motivo = motivo,
                        Salteo = $"hay {libreGB:F1} GB libres; se limpiará solo con menos de " +
                                 $"{_local.EspacioLibreMinimoGB:F1} GB"
                    });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DriveNotFoundException or ArgumentException)
            {
                return Guardar(new Depuracion
                {
                    Motivo = motivo,
                    Salteo = "no se pudo comprobar el espacio libre del disco"
                });
            }
        }

        // Con descargas O reenvíos en curso no se borra nada: los reenvíos también escriben dentro de
        // la carpeta de descargas (reenvio/{jobId}) y antes el repaso los podía dejar a medias.
        var descargas = _trabajos.TrabajosVivos();
        var reenvios = _reenvios.TrabajosVivos();
        var hayTrabajo = descargas > 0 || reenvios > 0;
        if (hayTrabajo)
            _log.LogInformation(
                "Depuración DICOM en pausa: {D} descarga(s) y {R} reenvío(s) en curso.", descargas, reenvios);

        var r = DepuradorDescargas.Ejecutar(
            _dicom.OutputRoot,
            TimeSpan.FromHours(_local.ConservarHoras),
            _local.ZipRetentionHours <= 0 ? TimeSpan.FromDays(36500) : TimeSpan.FromHours(_local.ZipRetentionHours),
            (long)_local.MaxMB * 1048576L,
            hayTrabajo,
            motivo);
        return Guardar(r);
    }

    private Depuracion Guardar(Depuracion r)
    {
        Ultima = r;
        if (r.Salteo is not null && r.ArchivosBorrados == 0)
            _log.LogInformation("Depuración DICOM ({Motivo}): {Resumen}.", r.Motivo, r.Resumen);
        else
            _log.LogInformation("Depuración DICOM ({Motivo}): {Resumen} en {Carpeta}.", r.Motivo, r.Resumen, _dicom.OutputRoot);
        return r;
    }

    private static async Task Esperar(TimeSpan tiempo, CancellationToken ct)
    {
        try { await Task.Delay(tiempo, ct); } catch (TaskCanceledException) { }
    }
}
