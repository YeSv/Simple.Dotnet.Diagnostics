using Microsoft.Extensions.Logging;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;
using Simple.Dotnet.Diagnostics.Streams;

namespace Simple.Dotnet.Diagnostics.Actions;

public static class Tasks
{
    // Assumes that both tasks can't throw and safe to cancel
    public static async Task<T> Either<T>(
        Func<CancellationToken, Task<T>> first, 
        Func<CancellationToken, Task<T>> second, 
        CancellationToken token)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        
        var (firstTask, secondTask) = (first(cts.Token), second(cts.Token));

        var issuer = await Task.WhenAny(firstTask, secondTask);
        cts.Cancel();

        await Task.WhenAll(firstTask, secondTask);

        return issuer.Result;
    }
}

public static class ActionTypes
{
    public static IAction OneShot(string name, ILogger logger, Func<IStream> stream, Func<Subscription> subscription) =>
        new Action(name, logger, new() { Retries = 1 }, stream, subscription);

    public static IAction NonStop(string name, ILogger logger, Func<IStream> stream, Func<Subscription> subscription) =>
        new Action(name, logger, new() { Retries = int.MaxValue }, stream, subscription);

    public static IAction Custom(string name, ILogger logger, ActionConfig config, Func<IStream> stream, Func<Subscription> subscription) =>
        new Action(name, logger, config, stream, subscription);
}