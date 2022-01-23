using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Actions;

public interface IAction
{
    Task<UniResult<Unit, Exception>> Execute(CancellationToken token);
}

public sealed class ActionConfig
{
    public int Retries { get; set; } = int.MaxValue; // Never stop retrying
    public TimeSpan RetryPause { get; set; } = TimeSpan.FromSeconds(10);
}