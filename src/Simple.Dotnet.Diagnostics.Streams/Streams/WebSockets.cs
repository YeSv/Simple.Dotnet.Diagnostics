using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System.Net.WebSockets;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.Streams.Streams;

public enum WebSocketResultType : byte { ClosedByClient, WsException, UnhandledException }

public readonly record struct WebSocketResult(WebSocketResultType Type, WebSocketError? Error, Exception? Exception);

public readonly record struct WebSocketSubscription(Task<WebSocketResult> Task);

public sealed class WebSocketStream : IStream
{
    readonly Task _reader;
    readonly WebSocket _ws;
    readonly JsonSerializerOptions? _jsonOptions;
    readonly CancellationTokenSource _cts = new();
    readonly TaskCompletionSource<WebSocketResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WebSocketStream(WebSocket ws, JsonSerializerOptions? jsonOptions)
    {
        _ws = ws;
        _jsonOptions = jsonOptions;
        _reader = Reader(ws, _cts, _tcs);
    }

    public Task<WebSocketResult> Completion => _tcs.Task.ContinueWith((t, s) =>
    {
        var stream = (WebSocketStream)s!;

        stream._cts.Cancel(); // Stop the thread
        stream._reader.Wait(); // Wait for the stop

        return t.Result;
    }, this);

    public ValueTask<UniResult<Unit, Exception>> Send(EventMetric metric, CancellationToken token)
    {
        var formatResult = Format(metric, _jsonOptions);
        if (formatResult.IsOk) return new(Send(formatResult.Ok, token));

        // Failed to format
        _tcs.TrySetResult(new(WebSocketResultType.UnhandledException, null, formatResult.Error)); // Resolve completion as error
        return new(UniResult.Error<Unit, Exception>(formatResult.Error!));
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<EventMetric> batch, CancellationToken token)
    {
        if (batch.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 1) return Send(batch.Span[0], token);

        var formatResult = Format(batch, _jsonOptions);
        if (formatResult.IsOk) return new(Send(formatResult.Ok, token));

        // Failed to format
        _tcs.TrySetResult(new(WebSocketResultType.UnhandledException, null, formatResult.Error)); // Resolve completion as error
        return new(UniResult.Error<Unit, Exception>(formatResult.Error!));
    }

    async Task<UniResult<Unit, Exception>> Send(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Binary, true, token); 
            return new(Unit.Shared);
        }
        catch (OperationCanceledException ex) 
        {
            _tcs.TrySetResult(new(WebSocketResultType.ClosedByClient, null, ex)); 
            return new(Unit.Shared);
        }
        catch (WebSocketException ex)
        {
            _tcs.TrySetResult(new(WebSocketResultType.WsException, ex.WebSocketErrorCode, ex));
            return new(ex);
        }
    }

    static Result<ReadOnlyMemory<byte>, Exception> Format(in EventMetric metric, JsonSerializerOptions? options)
    {
        try
        {
            using var writer = BufferWriterPool<byte>.Shared.Get();

            JsonSerializer.Serialize(new Utf8JsonWriter(writer.Value), metric, options);
            return new(writer.Value.WrittenSpan.ToArray());
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    static Result<ReadOnlyMemory<byte>, Exception> Format(ReadOnlyMemory<EventMetric> batch, JsonSerializerOptions? options)
    {
        try
        {
            using var writer = BufferWriterPool<byte>.Shared.Get();
            JsonSerializer.Serialize(new Utf8JsonWriter(writer.Value), batch, options);

            return new(writer.Value.WrittenSpan.ToArray());
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    static Task Reader(WebSocket ws, CancellationTokenSource cts, TaskCompletionSource<WebSocketResult> tcs) => Task.Run(async () =>
    {
        var token = cts.Token;
        var buffer = new ArrayBufferWriter<byte>(1024); // buffer to read messages
        try
        {
            while (!token.IsCancellationRequested)
            {
                var rcvTask = ws.ReceiveAsync(buffer.GetMemory(), token);
                var message = rcvTask.IsCompletedSuccessfully ? rcvTask.Result : await rcvTask;

                if (message.MessageType != WebSocketMessageType.Close)
                {
                    buffer.Clear(); // One way channel, we are not interested in any message except the close event so drop the bytes written from the client
                    continue;
                }

                tcs.TrySetResult(new(WebSocketResultType.ClosedByClient, default, default));
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            tcs.TrySetResult(new(WebSocketResultType.WsException, ex.WebSocketErrorCode, ex));
        }
        catch (Exception ex)
        {
            tcs.TrySetResult(new(WebSocketResultType.UnhandledException, default, ex));
        }
    }, cts.Token);
}
