using Simple.Dotnet.Diagnostics.Actions;
using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Diagnostics.Streams.Streams;

namespace Simple.Dotnet.Diagnostics.Host.AspNetCore.HostedServices;

public sealed class LoggerCountersHostedService : BackgroundService
{
    readonly ActionsRegistry _registry;
    readonly IConfiguration _configuration;
    readonly ILogger<LoggerCountersHostedService> _logger;

    public LoggerCountersHostedService(
        ActionsRegistry registry,
        IConfiguration configuration, 
        ILogger<LoggerCountersHostedService> logger) =>
        (_registry, _configuration, _logger) = (registry, configuration, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var appConfig = _configuration.GetSection(nameof(AppConfig)).Get<AppConfig?>() ?? new();
        for (var i = 0; i < appConfig.StartupCounters.Length; i++)
        {
            var counterConfig = appConfig.StartupCounters[i];
            if (counterConfig.StreamType != "logger") continue;

            var counters = new CountersQuery(
                counterConfig.CountersQuery!.ProcessId,
                counterConfig.CountersQuery.Providers,
                counterConfig.CountersQuery.ProcessName,
                counterConfig.CountersQuery.RefreshInterval);

            var action = ActionTypes.NonStop(
                counterConfig.ActionName,
                _logger,
                () => new LoggerStream(_logger), () =>
                {
                    var (subscription, error) = Counters.Handle(ref counters, CancellationToken.None);
                    if (subscription == null) throw new Exception($"Failed to create counters subscription. Action: {counterConfig.ActionName}. Error: {error.Validation ?? error.Exception!.Message}", error.Exception);

                    return subscription;
                });

            var registerResult = await _registry.Register(counterConfig.ActionName, action);
            if (!registerResult.IsOk) _logger.LogWarning(registerResult.Error, "Failed to schedule an action. Action: {Action}", counterConfig.ActionName);
        }
    }
}
