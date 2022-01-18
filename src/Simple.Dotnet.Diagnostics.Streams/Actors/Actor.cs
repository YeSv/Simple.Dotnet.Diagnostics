using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simple.Dotnet.Utilities.Buffers;
using System.Threading.Channels;

namespace Simple.Dotnet.Diagnostics.Streams.Actors;

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
