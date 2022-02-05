using Microsoft.Extensions.Diagnostics.HealthChecks;
using Simple.Dotnet.Diagnostics.Actions.Registry;

namespace Simple.Dotnet.Diagnostics.Host.AspNetCore.Health;

public sealed class ActionsHealthCheck : IHealthCheck
{
    static readonly HealthCheckResult Healthy = HealthCheckResult.Healthy("All actions are healthy");

    readonly ActionsRegistry _registry;
    readonly ILogger<ActionsHealthCheck> _logger;

    public ActionsHealthCheck(ILogger<ActionsHealthCheck> logger, ActionsRegistry registry) =>
        (_logger, _registry) = (logger, registry);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var (actions, error) = await _registry.GetActions();
        if (error != null)
        {
            _logger.LogWarning(error, "Failed to get actions from registry");
            return HealthCheckResult.Unhealthy("Failed to get actions from registry", error);
        }

        if (actions!.Length == 0) return Healthy;

        var details = default(Dictionary<string, object>);
        foreach (var (name, health) in actions)
        {
            if (health.Status == HealthStatus.Healthy) continue;
            (details ??= new())[name] = health.Description!;
        }

        if (details != null) 
            _logger.LogInformation($"{nameof(ActionsHealthCheck)} returns {nameof(HealthCheckResult.Degraded)} status. Some actions are unhealthy");

        return details switch
        {
            null => Healthy,
            _ => HealthCheckResult.Degraded("Some actions are unhealthy", data: details)
        };
    }
}
