using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Interceptors;

namespace Simple.Dotnet.Diagnostics.ServerSentEvents;

public sealed class ServerSentEventsInterceptor : IInterceptor
{
    public void Intercept(WebApplicationBuilder builder) { }

    public void Intercept(WebApplication application) => 
        application.MapGet("actions/counters/sse", (
            [FromQuery(Name = "pid")] int? processId,
            [FromQuery(Name = "pname")] string? processName,
            [FromQuery(Name = "providers")] string? providers,
            [FromQuery(Name = "interval")] uint? refreshInterval,
            [FromServices] ILogger<ServerSentEventsCounters> logger,
            [FromServices] ActionsRegistry registry,
            HttpContext context,
            CancellationToken token) => ServerSentEventsCounters.Handle(new(processId, providers, processName, refreshInterval), context, registry, logger, token));
}