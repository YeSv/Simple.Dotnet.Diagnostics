using Microsoft.Extensions.Diagnostics.HealthChecks;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Actions.Registry;

internal enum RegistryCmdType : byte { Schedule, Cancel, GetAll }

internal interface IRegistryCommand 
{ 
    RegistryCmdType Type { get; } 
}

internal sealed record ScheduleActionCommand(string ActionName, IAction Action) : IRegistryCommand
{
    public RegistryCmdType Type => RegistryCmdType.Schedule;
    public TaskCompletionSource<Task<UniResult<Unit, Exception>>> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record CancelActionCommand(string ActionName) : IRegistryCommand
{
    public RegistryCmdType Type => RegistryCmdType.Cancel; 
    public TaskCompletionSource<UniResult<Unit, Exception>> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record GetAllActionsCommand() : IRegistryCommand
{
    public RegistryCmdType Type => RegistryCmdType.GetAll;
    public TaskCompletionSource<Result<(string Name, HealthCheckResult Health)[], Exception>> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}