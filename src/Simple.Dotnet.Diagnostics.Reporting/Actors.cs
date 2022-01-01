using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simple.Dotnet.Utilities.Buffers;
using System.Threading.Channels;

namespace Simple.Dotnet.Diagnostics.Streams;

internal enum RootCommandType : byte { Add, Remove, Send }

internal record struct RootCommand(RootCommandType Type, StreamData? Data, TaskCompletionSource<object?>? Notification);

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

        var tcs = async ? null : new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var returnTask = async ? new ValueTask<Guid>(actor.Id) : new(tcs!.Task.ContinueWith((_, s) => ((Actor)s!).Id, actor));

        _channel.Writer.TryWrite(new(RootCommandType.Add, new(actor, actor.Id), tcs));

        return returnTask;
    }

    public ValueTask RemoveStream(Guid id, bool async = true)
    {
        var tcs = async ? null : new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = async ? ValueTask.CompletedTask : new(tcs!.Task);

        _channel.Writer.TryWrite(new(RootCommandType.Remove, new(null, id), tcs));
        
        return task;
    }

    public ValueTask Send<T>(T? data, Guid id, bool async = true) where T : class
    {
        var tcs = async ? null : new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                            var actor = (Actor)cmd.Data!.Value.Data!;
                            _actors.Add(actor.Id, actor);
                            break;

                        case RootCommandType.Remove:
                            if (_actors.Remove(cmd.Data!.Value.SubscriptionId, out var removedActor)) removedActor.Dispose();
                            break;

                        case RootCommandType.Send:
                            if (_actors.TryGetValue(cmd.Data!.Value.SubscriptionId, out var foundActor)) foundActor.Send(cmd.Data.Value!);
                            break;
                    }
                    cmd.Notification?.TrySetResult(null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Unhandled exception occurred in RootActors reader. CommandType: {CommandType}. Destination Actor: {ActorId}", cmd.Type, cmd.Data?.SubscriptionId);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}

internal sealed class Actor : IDisposable
{
    readonly Task _reader;
    readonly IStream _stream;
    readonly ILogger _logger;
    readonly Channel<StreamData> _channel;
    readonly IOptionsMonitor<RootActorConfig> _actorConfig;

    readonly CancellationTokenSource _cts = new();

    public Actor(
        ILogger logger, 
        IStream stream,
        IOptionsMonitor<RootActorConfig> actorConfig)
    {
        _logger = logger;
        _stream = stream;
        _actorConfig = actorConfig;
        _channel = Channel.CreateBounded<StreamData>(new BoundedChannelOptions(_actorConfig.CurrentValue.ChannelCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true, // Only _reader task is reading
            SingleWriter = true // Only root actor writes to a channel
        });

        _reader = Start();
    }

    public Guid Id { get; } = Guid.NewGuid();

    // By design channel is bounded and can lose data because metrics data is outdated and it's better not to retry
    public bool Send(in StreamData data) => _channel.Writer.TryWrite(data);

    public void Dispose()
    {
        _channel?.Writer.Complete();
        _cts?.Cancel();
        _reader?.Wait();
    }

    async Task Start()
    {
        var token = _cts.Token;
        var reader = _channel.Reader;
        var bufferWriter = new ArrayBufferWriter<StreamData>(_actorConfig.CurrentValue.StreamBatchSize);

        try
        {
            while (await reader.WaitToReadAsync(token))
            {
                try
                {
                    while (!bufferWriter.IsFull && reader.TryRead(out var data)) bufferWriter.Append(data); // Append untill available or batch is full

                    var result = await (bufferWriter.Written switch
                    {
                        1 => _stream.Send(bufferWriter.WrittenSpan[0], token),
                        _ => _stream.Send(bufferWriter.WrittenMemory, token)
                    });

                    if (!result.IsOk) _logger.LogError(result.Error, "Received error from a stream. ActorId: {ActorId}", Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Unhandled error occurred in actor's reader thread. ActorId: {ActorId}", Id);
                }
                finally
                {
                    bufferWriter.Clear();
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}