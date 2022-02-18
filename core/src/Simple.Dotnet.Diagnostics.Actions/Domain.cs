using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Actions;

public readonly record struct ActionHealthResult(bool IsHealthy, string? Reason, Exception? Error)
{
    public static ActionHealthResult Healthy(string? reason = null) => new(true, reason, null);
    public static ActionHealthResult Unhealthy(string reason, Exception? exception = null) => new(false, reason, exception);
}

public interface IAction
{
    ActionHealthResult GetHealth();
    Task<UniResult<Unit, Exception>> Execute(CancellationToken token);
}