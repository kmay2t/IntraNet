using Meditechnosys.Intranet.Models;

namespace Meditechnosys.Intranet.Services;

/// Describe que almacen de usuarios quedo activo en este arranque y por que.
/// La intranet prefiere MySQL (Auth:Store = "mysql"); si el servidor no responde al
/// arrancar, se degrada al almacen JSON para no tumbar el servicio y este registro
/// permite verlo en /api/status y en la pagina de Administracion.
public sealed class AuthStoreInfo
{
    /// Proveedor pedido en la configuracion ("mysql" o "json").
    public string Requested { get; }
    /// Proveedor realmente registrado ("mysql" o "json").
    public string Provider { get; }
    /// true si se pidio mysql pero no estaba disponible y se uso json.
    public bool Degraded { get; }
    /// Motivo de la degradacion (mensaje de la conexion fallida), si aplica.
    public string? Detail { get; }

    public AuthStoreInfo(string requested, string provider, bool degraded, string? detail = null)
    {
        Requested = requested;
        Provider = provider;
        Degraded = degraded;
        Detail = detail;
    }

    public string ProviderLabel => Provider == "mysql" ? "MySQL" : "Archivo JSON";

    /// Estado para el panel de sistemas y /api/status.
    public SystemStatus Status() => Degraded
        ? new SystemStatus("Usuarios (almacén)", "warn", "JSON · MySQL no disponible")
        : new SystemStatus("Usuarios (almacén)", "ok", ProviderLabel);
}
