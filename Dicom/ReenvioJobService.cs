using System.Collections.Concurrent;

namespace Meditechnosys.Intranet.Dicom;

/// <summary>
/// Registro en memoria de los reenvíos en curso (traer de un nodo y mandar a otro).
/// Hace lo mismo que <see cref="DownloadJobService"/> con las descargas: el trabajo corre en segundo
/// plano, la pantalla pregunta cada medio segundo por su progreso y puede cancelarlo.
/// Se retira de la memoria dos horas después de terminar para no acumular objetos.
/// </summary>
public class ReenvioJobService
{
    private sealed class Registro
    {
        public required ReenvioContexto Ctx { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public string Estado { get; set; } = "pending";
        public string Error { get; set; } = "";
    }

    private readonly ConcurrentDictionary<string, Registro> _jobs = new();

    public (string JobId, ReenvioContexto Ctx, CancellationToken Token) Crear(int estudios)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var ctx = new ReenvioContexto(id, estudios);
        var cts = new CancellationTokenSource();
        _jobs[id] = new Registro { Ctx = ctx, Cts = cts };
        return (id, ctx, cts.Token);
    }

    public bool Existe(string jobId) => _jobs.ContainsKey(jobId);

    /// <summary>
    /// ¿Hay algún reenvío en curso? Lo necesita <see cref="LimpiadorDescargas"/>: un reenvío escribe
    /// sus .dcm en la misma carpeta de descargas (reenvio/{jobId}), así que sin este aviso el
    /// repaso automático podía borrar imágenes que todavía estaban llegando.
    /// </summary>
    public bool TieneTrabajoActivo() => TrabajosVivos() > 0;

    /// <summary>Reenvíos pendientes o corriendo ahora mismo.</summary>
    public int TrabajosVivos()
    {
        var n = 0;
        foreach (var r in _jobs.Values)
            if (r.Estado is "pending" or "running") n++;
        return n;
    }

    /// <summary>Estado + progreso de un reenvío. false si el trabajo ya no está en memoria.</summary>
    public bool TryGet(string jobId, out ReenvioProgreso? progreso)
    {
        progreso = null;
        if (!_jobs.TryGetValue(jobId, out var r)) return false;

        var p = r.Ctx.Copia();
        p.Estado = r.Estado;
        p.Error = r.Error;
        p.Porcentaje = r.Estado switch
        {
            "completed" => 100,
            "failed" or "cancelled" => p.Porcentaje,
            _ => p.EstudiosTotal > 0
                ? Math.Clamp((int)Math.Round(p.EstudiosHechos * 95.0 / p.EstudiosTotal), 0, 95)
                : 0
        };
        progreso = p;
        return true;
    }

    public void MarcarCorriendo(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var r)) r.Estado = "running";
    }

    public void Completar(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var r))
        {
            r.Estado = r.Ctx.Errores > 0 ? "completed_con_errores" : "completed";
            r.Ctx.Fase = "done";
        }
        ProgramarLimpieza(jobId);
    }

    public void Fallar(string jobId, string error)
    {
        if (_jobs.TryGetValue(jobId, out var r))
        {
            r.Estado = "failed";
            r.Error = error;
            r.Ctx.Mensaje("ERROR: " + error);
        }
        ProgramarLimpieza(jobId);
    }

    /// <summary>Detiene el reenvío: ya no se piden los estudios que faltaban.</summary>
    public bool Cancelar(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var r)) return false;
        if (r.Estado is "completed" or "failed" or "cancelled" or "completed_con_errores") return false;

        r.Estado = "cancelled";
        r.Ctx.Mensaje("Cancelado por el usuario.");
        try { r.Cts.Cancel(); } catch (ObjectDisposedException) { /* ya liberado */ }
        ProgramarLimpieza(jobId);
        return true;
    }

    private void ProgramarLimpieza(string jobId)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromHours(2));
            if (_jobs.TryRemove(jobId, out var r)) r.Cts.Dispose();
        });
    }
}
