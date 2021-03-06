using Microsoft.Extensions.Logging;
using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Diagnostics.Streams;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System.Threading.Channels;

namespace Simple.Dotnet.Diagnostics.Actions;

public sealed class CountersActionConfig
{
    public int Retries { get; set; } = int.MaxValue; // Never stop retrying
    public TimeSpan RetryPause { get; set; } = TimeSpan.FromSeconds(10);

    public int StreamBatchSize { get; set; } = 100;
    public int ChannelCapacity { get; set; } = 100; // Magic number, newer messages will remove older ones
}

public sealed class CountersAction : IAction
{
    ActionHealthResult _health = new(true, null, null);

    readonly ILogger _logger;
    readonly CountersActionConfig _config;
    readonly Func<IStream> _stream;
    readonly Func<CountersSubscription> _counters;

    public CountersAction(
        string name,
        ILogger logger,
        CountersActionConfig config,
        Func<IStream> stream,
        Func<CountersSubscription> counters)
    {
        Name = name;

        _logger = logger;
        _config = config;
        _stream = stream;
        _counters = counters;
    }

    public string Name { get; }

    public ActionHealthResult GetHealth() => _health;

    public Task<UniResult<Unit, Exception>> Execute(CancellationToken token) => Task.Run(async () =>
    {
        var (retries, pause) = (_config.Retries, _config.RetryPause);
        var result = UniResult.Ok<Unit, Exception>(Unit.Shared);

        while (!token.IsCancellationRequested && retries-- >= 0)
        {
            _health = ActionHealthResult.Healthy("Started a new execution cycle");

            result = await ExecuteOnce(token);

            if (!result.IsOk)
            {
                _health = ActionHealthResult.Unhealthy("Execution failed with unhandled error", result.Error);
                _logger.LogError(result.Error!, "Action execution failed with unhandled error. Action: {Action}", Name);
            }

            await Task.Delay(pause, token).ContinueWith((t, n) =>
            {
                if (t.Exception != null && !t.IsCanceled) _logger.LogInformation(t.Exception!, "Delay failed with an exception. Action: {Action}", (string)n!);
            }, Name);
        }

        return result;
    }, token);

    async Task<UniResult<Unit, Exception>> ExecuteOnce(CancellationToken token)
    {
        try
        {
            var stream = _stream();
            var subscription = _counters();
            var channel = CreateChannel(_config.ChannelCapacity);

            var result = await Tasks.Either(
                token => CreateReader(channel, stream, _config.ChannelCapacity, token),
                token => subscription.Start(m => channel.Writer.TryWrite(m), token),
                token);

            return result;
        }
        catch (OperationCanceledException)
        {
            return new(Unit.Shared);
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    static Channel<CounterMetric> CreateChannel(int capacity) => Channel.CreateBounded<CounterMetric>(new BoundedChannelOptions(capacity)
    {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    Task<UniResult<Unit, Exception>> CreateReader(Channel<CounterMetric> channel, IStream stream, int batchSize, CancellationToken token) => Task.Run(async () =>
    {
        var reader = channel.Reader;
        var bufferWriter = new ArrayBufferWriter<CounterMetric>(batchSize);

        try
        {
            while (await reader.WaitToReadAsync(token))
            {
                try
                {
                    while (!bufferWriter.IsFull && reader.TryRead(out var data)) bufferWriter.Append(data); // Append untill available or batch is full

                    var result = await (bufferWriter.Written switch
                    {
                        1 => stream.Send(bufferWriter.WrittenSpan[0], token),
                        _ => stream.Send(bufferWriter.WrittenMemory, token)
                    });

                    if (!result.IsOk) return result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new(ex);
                }
                finally
                {
                    bufferWriter.Clear();
                }
            }
        }
        catch (OperationCanceledException) { }

        return new(Unit.Shared);
    }, token);
}
