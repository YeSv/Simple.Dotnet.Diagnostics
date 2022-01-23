using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simple.Dotnet.Utilities.Pools;
using System.Threading.Channels;

namespace Simple.Dotnet.Diagnostics.Streams.Actors;

internal enum RootCommandType : byte { Add, Remove, Send }

internal record struct RootCommand(RootCommandType Type, StreamData Data, TaskCompletionSource? Notification);

public sealed record RootActorConfig(int ChannelCapacity, int StreamBatchSize);

public sealed class RootActor : IDisposable
{
    readonly Task _reader;
    readonly ILogger _logger;
    readonly Channel<RootCommand> _channel;
    readonly IOptionsMonitor<RootActorConfig> _actorConfig;

    readonly CancellationTokenSource _cts = new();
    readonly Dictionary<Guid, Actor> _actors = new();

    public RootActor(ILogger logger, IOptionsMonitor<RootActorConfig> config)
    {
        _logger = logger;
        _actorConfig = config;
        _channel = Channel.CreateUnbounded<RootCommand>(new() { SingleReader = true, AllowSynchronousContinuations = false });

        _reader = Start();
    }

    public ValueTask<Guid> AddStream(IStream stream, bool async = true)
    {
        var actor = new Actor(_logger, stream, _actorConfig);

        var tcs = async ? null : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = async ? new ValueTask<Guid>(actor.Id) : new(tcs!.Task.ContinueWith((_, s) => ((Actor)s!).Id, actor));

        _channel.Writer.TryWrite(new(RootCommandType.Add, new(new(actor, (_, _) => { }), actor.Id), tcs));

        return task;
    }

    public ValueTask RemoveStream(Guid id, bool async = true)
    {
        var tcs = async ? null : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = async ? ValueTask.CompletedTask : new(tcs!.Task);

        _channel.Writer.TryWrite(new(RootCommandType.Remove, new(new(), id), tcs));

        return task;
    }

    public ValueTask Send(ValueRent<object?> data, Guid id, bool async = true)
    {
        var tcs = async ? null : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = async ? ValueTask.CompletedTask : new(tcs!.Task);

        _channel.Writer.TryWrite(new(RootCommandType.Send, new(data, id), tcs));

        return task;
    }

    public void Dispose()
    {
        _channel?.Writer.Complete();
        _cts?.Cancel();
        _reader?.Wait();
        foreach (var (_, actor) in _actors) actor.Dispose();
    }

    async Task Start()
    {
        try
        {
            await foreach (var cmd in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    switch (cmd.Type)
                    {
                        case RootCommandType.Add:
                            var actor = (Actor)cmd.Data.Rent.Value!; // OMG :D
                            _actors.Add(actor.Id, actor);
                            break;

                        case RootCommandType.Remove:
                            if (_actors.Remove(cmd.Data.SubscriptionId, out var removedActor)) removedActor.Dispose();
                            break;

                        case RootCommandType.Send:
                            if (_actors.TryGetValue(cmd.Data.SubscriptionId, out var foundActor)) foundActor.Send(cmd.Data);
                            break;
                    }
                    cmd.Notification?.TrySetResult();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Unhandled exception occurred in RootActors reader. CommandType: {CommandType}. Destination Actor: {ActorId}", cmd.Type, cmd.Data.SubscriptionId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
