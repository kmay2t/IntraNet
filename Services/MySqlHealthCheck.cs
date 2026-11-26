using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;

namespace Meditechnosys.Intranet.Services;

/// <summary>
/// Comprueba solo lo necesario para aceptar tráfico: que MySQL responda un SELECT 1. No consulta
/// el PACS ni genera datos de pacientes y usa un tiempo de espera corto para no acumular sondeos.
/// </summary>
public sealed class MySqlHealthCheck(IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var cs = config.GetConnectionString("Usuarios");
        if (string.IsNullOrWhiteSpace(cs))
            return HealthCheckResult.Unhealthy("No está configurada la base de usuarios.");

        try
        {
            var builder = new MySqlConnectionStringBuilder(cs)
            {
                ConnectionTimeout = 2,
                DefaultCommandTimeout = 2
            };
            await using var cn = new MySqlConnection(builder.ConnectionString);
            await cn.OpenAsync(cancellationToken);
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("MySQL responde.");
        }
        catch (Exception e) when (e is MySqlException or InvalidOperationException or OperationCanceledException)
        {
            // No se adjunta la excepción al resultado: evita filtrar servidores/puertos y que un
            // sondeo repetido llene el registro con la misma pila de errores.
            return HealthCheckResult.Unhealthy("MySQL no está disponible.");
        }
    }
}
