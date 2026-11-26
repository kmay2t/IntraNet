using System.Threading.Channels;
using Meditechnosys.Intranet.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meditechnosys.Intranet.Services;

internal sealed record AuditWrite(string Usuario, string Evento, string Detalle, string Ip, string Dispositivo);

/// <summary>
/// Cola acotada compartida por quien registra y por el worker que escribe. Si se llena, el
/// registro hace una escritura directa de respaldo para no perder el evento ni dejar crecer
/// la memoria sin límite.
/// </summary>
public sealed class AuditQueue
{
    internal Channel<AuditWrite> Channel { get; }

    public AuditQueue(IHostApplicationLifetime lifetime, IConfiguration config)
    {
        var capacity = Math.Clamp(config.GetValue<int?>("Audit:QueueCapacity") ?? 5000, 100, 20_000);
        Channel = System.Threading.Channels.Channel.CreateBounded<AuditWrite>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        lifetime.ApplicationStopping.Register(() => Channel.Writer.TryComplete());
    }
}

/// <summary>
/// Fachada de la bitácora usada por las peticiones. Captura IP y dispositivo antes de devolver
/// el control y encola el evento; las lecturas se delegan al almacén real.
/// </summary>
public sealed class QueuedAuditLog(
    [FromKeyedServices("audit-sink")] IAuditLog inner,
    AuditQueue queue,
    IHttpContextAccessor http,
    ILogger<QueuedAuditLog> logger) : IAuditLog
{
    public void Register(string usuario, string evento, string detalle = "", string? ip = null,
        string? dispositivo = null)
    {
        var ctx = http.HttpContext;
        var ipReal = string.IsNullOrWhiteSpace(ip)
            ? ctx?.Connection.RemoteIpAddress?.ToString()
            : ip;
        var dispReal = string.IsNullOrWhiteSpace(dispositivo)
            ? Dispositivo.Describir(ctx?.Request.Headers.UserAgent.ToString())
            : dispositivo;
        var item = new AuditWrite(
            Recortar(usuario, 40),
            Recortar(evento, 60),
            Recortar(detalle, 255),
            Recortar(ipReal, 45),
            Recortar(dispReal, Dispositivo.Max));

        if (queue.Channel.Writer.TryWrite(item)) return;

        // La cola está al límite: se prefiere un registro tardío a perderlo. El almacén ya
        // encapsula sus errores, así que esta salida de emergencia tampoco rompe la petición.
        logger.LogWarning("La cola de auditoría está llena; se escribe el evento directamente.");
        inner.Register(item.Usuario, item.Evento, item.Detalle, item.Ip, item.Dispositivo);
    }

    public IReadOnlyList<AuditEntry> Query(string? usuario = null, DateTime? dia = null, int limite = 500,
        DateTime? desde = null, DateTime? hasta = null) =>
        inner.Query(usuario, dia, limite, desde, hasta);

    public IReadOnlyList<IpUso> IpsRegistradas(int limite = 400) => inner.IpsRegistradas(limite);

    public IReadOnlyDictionary<string, int> DiasConMovimientos(string? usuario = null) =>
        inner.DiasConMovimientos(usuario);

    public IReadOnlyDictionary<string, AuditEntry> UltimosDetalles() => inner.UltimosDetalles();

    public IReadOnlyDictionary<string, DateTime> UltimosAccesos() => inner.UltimosAccesos();

    public IReadOnlyDictionary<string, DateTime> UltimasActividades() => inner.UltimasActividades();

    private static string Recortar(string? valor, int max) =>
        string.IsNullOrEmpty(valor) ? "" : valor.Length <= max ? valor : valor[..max];
}

/// <summary>
/// Vacía la bitácora fuera del hilo de las peticiones. Al detener el servicio intenta escribir
/// los eventos que quedaron encolados antes de terminar.
/// </summary>
public sealed class AuditWorker(
    [FromKeyedServices("audit-sink")] IAuditLog sink,
    AuditQueue queue,
    ILogger<AuditWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Channel.Reader.WaitToReadAsync(stoppingToken))
            {
                while (queue.Channel.Reader.TryRead(out var item))
                    Escribir(item);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // El host está deteniendo el servicio: se drena lo que ya estaba aceptado.
        }

        while (queue.Channel.Reader.TryRead(out var pendiente))
            Escribir(pendiente);
    }

    private void Escribir(AuditWrite item)
    {
        try
        {
            sink.Register(item.Usuario, item.Evento, item.Detalle, item.Ip, item.Dispositivo);
        }
        catch (Exception e)
        {
            // Los almacenes actuales ya no lanzan, pero el worker no debe desaparecer si uno
            // futuro rompe ese contrato.
            logger.LogWarning("No se pudo vaciar un evento de auditoría: {Error}", e.Message);
        }
    }
}
