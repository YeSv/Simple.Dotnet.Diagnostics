using Microsoft.Extensions.Diagnostics.HealthChecks;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Actions;

public interface IAction
{
    HealthCheckResult GetHealth();
    Task<UniResult<Unit, Exception>> Execute(CancellationToken token);
}