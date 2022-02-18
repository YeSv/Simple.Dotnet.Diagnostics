using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Actions.Registry;

internal enum RegistryCmdType : byte { Register, Cancel, GetAll }

internal interface IRegistryCommand 
{ 
    RegistryCmdType Type { get; } 
}

internal sealed record RegisterActionCommand(string ActionName, IAction Action) : IRegistryCommand
{
    public RegistryCmdType Type => RegistryCmdType.Register;
    public TaskCompletionSource<UniResult<Task<UniResult<Unit, Exception>>, Exception>> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record CancelActionCommand(string ActionName) : IRegistryCommand
{
    public RegistryCmdType Type => RegistryCmdType.Cancel; 
    public TaskCompletionSource<UniResult<Unit, Exception>> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record GetAllActionsCommand() : IRegistryCommand
{
    public RegistryCmdType Type => RegistryCmdType.GetAll;
    public TaskCompletionSource<Result<(string Name, ActionHealthResult Health)[], Exception>> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}