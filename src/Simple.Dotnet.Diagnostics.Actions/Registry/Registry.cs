using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System.Threading.Tasks.Dataflow;

namespace Simple.Dotnet.Diagnostics.Actions.Registry;

internal readonly record struct ActionOwner(IAction Action, CancellationTokenSource Cancel);

public sealed class ActionsRegistry
{
    readonly ActionBlock<IRegistryCommand> _processor;
    readonly Dictionary<string, ActionOwner> _actions = new();

    public ActionsRegistry() => _processor = new(c => _ = c.Type switch
    {
        RegistryCmdType.GetAll => GetAll((GetAllActionsCommand)c),
        RegistryCmdType.Schedule => Schedule((ScheduleActionCommand)c),
        RegistryCmdType.Cancel => Cancel((CancelActionCommand)c),
        _ => Unit.Shared
    }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

    public async Task<UniResult<Unit, Exception>> Schedule(string name, IAction action)
    {
        var cmd = new ScheduleActionCommand(name, action);
        _processor.Post(cmd);

        var scheduledTask = await cmd.Tcs.Task;
        return await scheduledTask.ContinueWith(t =>
        {
            Cancel(name);
            return t.Result;
        });
    }

    public Task<UniResult<Unit, Exception>> Cancel(string name)
    {
        var cmd = new CancelActionCommand(name);
        _processor.Post(cmd);

        return cmd.Tcs.Task;
    }

    public Task<Result<(string Name, HealthCheckResult Health)[], Exception>> GetActions()
    {
        var cmd = new GetAllActionsCommand();
        _processor.Post(cmd);

        return cmd.Tcs.Task;
    }

    Unit Schedule(ScheduleActionCommand cmd)
    {
        if (cmd.Action == null || cmd.ActionName == null)
        {
            cmd.Tcs.TrySetResult(Task.FromResult(UniResult.Error<Unit, Exception>(new("Action/ActionName can't be null"))));
            return Unit.Shared;
        }

        try
        {
            var cts = new CancellationTokenSource();
            _actions[cmd.ActionName] = new(cmd.Action, cts);

            cmd.Tcs.TrySetResult(cmd.Action.Execute(cts.Token));
        }
        catch (Exception ex)
        {
            cmd.Tcs.TrySetResult(Task.FromResult(UniResult.Error<Unit, Exception>(ex)));
        }

        return Unit.Shared;
    }

    Unit Cancel(CancelActionCommand cmd)
    {
        try
        {
            if (_actions.Remove(cmd.ActionName, out var owner)) owner.Cancel.Cancel();
            cmd.Tcs.TrySetResult(new(Unit.Shared));
        }
        catch (Exception ex)
        {
            cmd.Tcs.TrySetResult(new(ex));
        }

        return Unit.Shared;
    }

    Unit GetAll(GetAllActionsCommand cmd)
    {
        try
        {
            using var rent = new Rent<(string, HealthCheckResult)>(_actions.Count);
            foreach (var (name, owner) in _actions) rent.Append(new(name, owner.Action.GetHealth()));

            cmd.Tcs.TrySetResult(new(rent.WrittenSpan.ToArray()));
        }
        catch (Exception ex)
        {
            cmd.Tcs.TrySetResult(new(ex));
        }

        return Unit.Shared;
    }
}
