using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;
using Simple.Dotnet.Diagnostics.Streams;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Actions;

public interface IAction
{
    HealthCheckResult GetHealth();
    Task<UniResult<Unit, Exception>> Execute(CancellationToken token);
}