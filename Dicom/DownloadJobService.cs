using System.Collections.Concurrent;
using Meditechnosys.Intranet.Dicom;
using Microsoft.Extensions.Options;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>
/// Registro en memoria de trabajos de descarga con progreso.
/// Cada trabajo se retira de la lista seis horas después de terminar, para no acumular objetos.
///
/// Varias descargas pueden correr a la vez (hasta <c>Local:MaxDescargasSimultaneas</c>) porque la
/// pantalla ya no obliga a esperar a que termine una para pedir otra. Las que llegan con el cupo
/// lleno quedan en "pending" (la pantalla dice «en cola») y arrancan solas cuando se libera un
/// hueco. El límite existe para no saturar el PACS: cada descarga abre sus propias asociaciones y
/// el tope real por nodo lo pone <c>MaxParallelPerNode</c>, compartido entre todas.
/// </summary>
public class DownloadJobService
{
    private readonly ConcurrentDictionary<string, DownloadProgress> _jobs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cts = new();
    private readonly SemaphoreSlim _huecos;
    private readonly int _maxTrabajosVivos;

    public DownloadJobService(IOptions<LocalOptions> local)
    {
        var o = local.Value;
        var simultaneas = o.MaxDescargasSimultaneas < 1 ? 1 : Math.Min(o.MaxDescargasSimultaneas, 8);
        _huecos = new SemaphoreSlim(simultaneas, simultaneas);
        _maxTrabajosVivos = Math.Max(simultaneas, o.MaxTrabajosEnCola < 1 ? 20 : o.MaxTrabajosEnCola);
        MaxSimultaneas = simultaneas;
    }

    /// <summary>Cuántas descargas se ejecutan a la vez (para el estado y para avisar en la pantalla).</summary>
    public int MaxSimultaneas { get; }

    public string CreateJob(int studiesRequested, int imagesExpected, string label = "")
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var progress = new DownloadProgress
        {
            JobId = id,
            Status = "pending",
            Label = label,
            StudiesRequested = studiesRequested,
            ImagesExpected = imagesExpected,
            Phase = "queued",
            Percent = 0
        };
        _jobs[id] = progress;
        _cts[id] = new CancellationTokenSource();
        return id;
    }

    public CancellationToken GetToken(string jobId)
    {
        return _cts.TryGetValue(jobId, out var cts) ? cts.Token : CancellationToken.None;
    }

    /// <summary>
    /// Pone el trabajo en cola y lo lanza cuando quede un hueco libre. Devuelve false si ya hay
    /// demasiados trabajos vivos (la pantalla debe pedir que se espere un poco).
    /// El cuerpo se ejecuta en un hilo de tareas: la petición HTTP responde de inmediato.
    /// </summary>
    public bool Encolar(string jobId, Func<CancellationToken, Task> cuerpo)
    {
        if (!_jobs.TryGetValue(jobId, out _)) return false;
        if (TrabajosVivos() > _maxTrabajosVivos) return false;

        _ = Task.Run(async () =>
        {
            var ct = GetToken(jobId);
            try
            {
                await _huecos.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;   // se canceló mientras esperaba su turno: ya quedó marcado como cancelled
            }

            try
            {
                Update(jobId, p =>
                {
                    if (p.Status == "cancelled") return;
                    p.Status = "running";
                    p.Phase = "retrieving";
                });
                await cuerpo(ct);
            }
            catch (OperationCanceledException)
            {
                MarkCancelled(jobId);
            }
            catch (Exception ex)
            {
                Fail(jobId, ex.Message);
            }
            finally
            {
                _huecos.Release();
            }
        });
        return true;
    }

    /// <summary>
    /// Cancela una descarga en curso: interrumpe los estudios que faltan y deja el job en
    /// "cancelled". Lo ya guardado en disco NO se borra (queda en la carpeta de salida), pero
    /// ya no se genera ZIP. Devuelve false si el job no existe o ya terminó.
    /// </summary>
    public bool Cancel(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var p)) return false;

        lock (p)
        {
            if (p.Status is "completed" or "failed" or "cancelled") return false;
            MarkCancelled(p);
            p.Messages.Add($"Cancelado por el usuario con {p.StudiesCompleted}/{p.StudiesRequested} estudios y {p.ImagesSaved} imágenes.");
        }

        if (_cts.TryGetValue(jobId, out var cts))
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* el job ya se limpió */ }
        }

        ScheduleCleanup(jobId);
        return true;
    }

    /// <summary>Marca el job como cancelado desde el propio trabajador (cancelación externa o de apagado).</summary>
    public void MarkCancelled(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var p)) return;
        lock (p)
        {
            if (p.Status is "completed" or "failed") return;
            MarkCancelled(p);
        }
        ScheduleCleanup(jobId);
    }

    private static void MarkCancelled(DownloadProgress p)
    {
        p.Status = "cancelled";
        p.Phase = "done";
        p.Error = null;
    }

    public bool TryGet(string jobId, out DownloadProgress? progress)
    {
        if (_jobs.TryGetValue(jobId, out var p))
        {
            progress = Snapshot(p);
            return true;
        }
        progress = null;
        return false;
    }

    /// <summary>
    /// ¿Hay alguna descarga sin terminar? Lo usa <see cref="LimpiadorDescargas"/> para no borrar
    /// nunca archivos de una transferencia en curso, aunque ya cumpla la hora de repaso.
    /// </summary>
    public bool TieneTrabajoActivo() => TrabajosVivos() > 0;

    /// <summary>Trabajos en curso o en cola (los "pending" también cuentan: ocupan su cupo de disco).</summary>
    public int TrabajosVivos()
    {
        var n = 0;
        foreach (var p in _jobs.Values)
        {
            lock (p)
            {
                if (p.Status is "pending" or "running") n++;
            }
        }
        return n;
    }

    /// <summary>
    /// Copia de los trabajos vivos, para que la pantalla vuelva a engancharse a las descargas en
    /// curso después de recargar la página o abrir el apartado desde otra ventana.
    /// </summary>
    public List<DownloadProgress> Activos()
    {
        var salida = new List<DownloadProgress>();
        foreach (var p in _jobs.Values)
        {
            lock (p)
            {
                if (p.Status is not ("pending" or "running")) continue;
            }
            var s = Snapshot(p);
            if (s is not null) salida.Add(s);
        }
        return salida.OrderBy(s => s.JobId, StringComparer.Ordinal).ToList();
    }

    /// <summary>Cuantos trabajos siguen en la lista de memoria (solo para el registro de estado).</summary>
    public int TrabajosEnMemoria => _jobs.Count;

    public void Update(string jobId, Action<DownloadProgress> mutator)
    {
        if (!_jobs.TryGetValue(jobId, out var p)) return;
        lock (p)
        {
            mutator(p);
            RecalcPercent(p);
        }
    }

    public void AddMessage(string jobId, string msg)
    {
        Update(jobId, p =>
        {
            p.Messages.Add(msg);
            if (p.Messages.Count > 40)
                p.Messages.RemoveRange(0, p.Messages.Count - 40);
        });
    }

    public void SetImages(string jobId, int images)
    {
        Update(jobId, p =>
        {
            if (p.Status == "cancelled") return;
            p.ImagesSaved = images;
            if (p.Phase == "queued" || p.Phase == "retrieving")
                p.Phase = "retrieving";
            p.Status = "running";
        });
    }

    public void IncrementStudies(string jobId)
    {
        Update(jobId, p =>
        {
            if (p.Status == "cancelled") return;
            p.StudiesCompleted++;
            p.Status = "running";
        });
    }

    /// <summary>Fase de empaquetado. No reactiva un job ya cancelado.</summary>
    public void MarkZipping(string jobId) => MarkPhase(jobId, "zipping");

    /// <summary>Cambia la fase mostrada (retrieving | verifying | retrying | zipping). No reactiva un cancelado.</summary>
    public void MarkPhase(string jobId, string phase)
    {
        Update(jobId, p =>
        {
            if (p.Status == "cancelled") return;
            p.Phase = phase;
            p.Status = "running";
        });
    }

    public void Complete(string jobId, DownloadResult result)
    {
        Update(jobId, p =>
        {
            // Si el usuario canceló, el resultado parcial no debe mostrarse como "Completado".
            if (p.Status == "cancelled")
            {
                p.StudiesCompleted = Math.Max(p.StudiesCompleted, result.StudiesCompleted);
                p.ImagesSaved = Math.Max(p.ImagesSaved, result.ImagesSaved);
                return;
            }
            p.Status = "completed";
            p.Phase = "done";
            p.Percent = 100;
            p.StudiesCompleted = result.StudiesCompleted;
            p.ImagesSaved = result.ImagesSaved;
            p.ZipFileName = result.ZipFileName;
            p.ZipDownloadUrl = result.ZipDownloadUrl;
            p.OutputPath = result.OutputPath;
            // Resultado de la comprobación de integridad y las partes del ZIP.
            p.Verified = result.Verified;
            p.IncompleteStudies = result.IncompleteStudies.ToList();
            p.MissingInstances = result.MissingInstances;
            p.RetriedStudies = result.RetriedStudies;
            p.ZipParts = result.ZipParts.ToList();
            // Solo últimos mensajes del resultado (pueden ser muchos en modo verbose)
            foreach (var m in result.Messages.TakeLast(30))
            {
                if (!p.Messages.Contains(m))
                    p.Messages.Add(m);
            }
            if (p.Messages.Count > 40)
                p.Messages.RemoveRange(0, p.Messages.Count - 40);
        });
        ScheduleCleanup(jobId);
    }

    public void Fail(string jobId, string error)
    {
        Update(jobId, p =>
        {
            // Una cancelación ya registrada no se reetiqueta como error.
            if (p.Status is "failed" or "cancelled") return;
            p.Status = "failed";
            p.Phase = "done";
            p.Error = error;
            p.Messages.Add("ERROR: " + error);
        });
        ScheduleCleanup(jobId);
    }

    private static void RecalcPercent(DownloadProgress p)
    {
        if (p.Status is "completed")
        {
            p.Percent = 100;
            return;
        }
        // Cancelado: se congela el avance tal como quedó al detenerse.
        if (p.Status is "cancelled") return;
        if (p.Phase is "queued" && p.Status is "pending") return;   // en cola: sigue en 0 %
        if (p.Phase == "zipping")
        {
            // 90–99 mientras se empaqueta
            p.Percent = Math.Min(99, 90 + (p.ImagesSaved > 0 ? 5 : 0));
            return;
        }
        if (p.Phase is "verifying" or "retrying")
        {
            // Comprobando / re-pidiendo lo que faltó: cerca del final, pero sin cerrar la barra.
            p.Percent = Math.Max(p.Percent, 92);
            return;
        }

        double byStudies = 0;
        if (p.StudiesRequested > 0)
            byStudies = Math.Min(1.0, (double)p.StudiesCompleted / p.StudiesRequested);

        double byImages = 0;
        if (p.ImagesExpected > 0)
            byImages = Math.Min(1.0, (double)p.ImagesSaved / p.ImagesExpected);

        // Con muchos estudios el contador de estudios es más fiable que el de imágenes
        // (NumberOfInstances del C-FIND a menudo viene vacío o incorrecto).
        double frac;
        if (p.StudiesRequested >= 50)
            frac = byStudies;
        else if (p.ImagesExpected > 0)
            frac = Math.Max(byImages, byStudies * 0.5);
        else
            frac = byStudies;

        p.Percent = (int)Math.Clamp(Math.Round(frac * 95), 0, 95);
    }

    private static DownloadProgress Snapshot(DownloadProgress p)
    {
        lock (p)
        {
            return new DownloadProgress
            {
                JobId = p.JobId,
                Status = p.Status,
                Label = p.Label,
                StudiesRequested = p.StudiesRequested,
                StudiesCompleted = p.StudiesCompleted,
                ImagesSaved = p.ImagesSaved,
                ImagesExpected = p.ImagesExpected,
                Percent = p.Percent,
                Phase = p.Phase,
                Messages = p.Messages.ToList(),
                ZipFileName = p.ZipFileName,
                ZipDownloadUrl = p.ZipDownloadUrl,
                Error = p.Error,
                OutputPath = p.OutputPath,
                Verified = p.Verified,
                IncompleteStudies = p.IncompleteStudies.ToList(),
                MissingInstances = p.MissingInstances,
                RetriedStudies = p.RetriedStudies,
                ZipParts = p.ZipParts.ToList()
            };
        }
    }

    private void ScheduleCleanup(string jobId)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromHours(6));
            _jobs.TryRemove(jobId, out _);
            if (_cts.TryRemove(jobId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        });
    }
}
