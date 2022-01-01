using Simple.Dotnet.Utilities.Buffers;
using System.Net.WebSockets;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.Streams.WebSockets;

public enum WebSocketResultType : byte { ClosedByClient, WsException, UnhandledException }

public readonly record struct WebSocketResult(WebSocketResultType Type, WebSocketError? Error, Exception? Exception);

public readonly record struct WebSocketSubscription(Task<WebSocketResult> Task);

public sealed class WebSocketStream : IStream
{
    Task? _reader;
    WebSocket? _ws;
    CancellationTokenSource? _cts;
    TaskCompletionSource<WebSocketResult>? _tcs;

    readonly JsonSerializerOptions? _jsonOptions;

    public WebSocketStream(JsonSerializerOptions? jsonOptions) => _jsonOptions = jsonOptions;

    public WebSocketSubscription Subscribe(WebSocket ws)
    {
        _ws = ws;
        _cts = new();
        _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader = Reader(_ws!, _cts!, _tcs!);

        return new(_tcs.Task);
    }

    public ValueTask Send(StreamData data, CancellationToken token)
    {
        if (_ws is null || data.Data is null) return ValueTask.CompletedTask;
        try
        {
            using var pooledData = data.Data;
            using var writerRent = BufferWriterPool<byte>.Shared.Get();

            JsonSerializer.Serialize(new Utf8JsonWriter(writerRent.Value), pooledData, _jsonOptions);
            return new(Send(writerRent.Value.WrittenSpan.ToArray(), token));
        }
        catch (Exception ex)
        {
            _tcs?.TrySetResult(new(WebSocketResultType.UnhandledException, null, ex));
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask Send(ReadOnlyMemory<StreamData> batch, CancellationToken token)
    {
        if (_ws is null) return ValueTask.CompletedTask;
        if (batch.Length == 0) return ValueTask.CompletedTask;
        if (batch.Length == 1) return Send(batch.Span[0], token);

        try
        {
            using var writerRent = BufferWriterPool<byte>.Shared.Get();
            using var poolableRent = new Rent<IPoolable>(batch.Length); // Get array of poolables from pool

            for (var i = 0; i < batch.Length; i++)
            {
                if (batch.Span[i].Data == null) continue;
                poolableRent.Append(batch.Span[i].Data);
            }

            JsonSerializer.Serialize(new Utf8JsonWriter(writerRent.Value), poolableRent.WrittenMemory, _jsonOptions);

            return new(Send(writerRent.Value.WrittenSpan.ToArray(), token));
        }
        catch (Exception ex)
        {
            _tcs?.TrySetResult(new(WebSocketResultType.UnhandledException, null, ex));
            return ValueTask.CompletedTask;
        }
        finally
        {
            for (var i = 0; i < batch.Length; i++) batch.Span[i].Data.Dispose(); // Return to pool
        }
    }

    async Task Send(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        try
        {
            await _ws!.SendAsync(bytes, WebSocketMessageType.Binary, true, token);
        }
        catch (OperationCanceledException ex) { }
        catch (WebSocketException ex)
        {
            _tcs!.TrySetResult(new(WebSocketResultType.WsException, ex.WebSocketErrorCode, ex));
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
                cts.Cancel();
            }
        }
        catch (OperationCanceledException ex) { }
        catch (WebSocketException ex)
        {
            tcs.TrySetResult(new(WebSocketResultType.WsException, ex.WebSocketErrorCode, ex));
            cts.Cancel();
        }
        catch (Exception ex)
        {
            tcs.TrySetResult(new(WebSocketResultType.UnhandledException, default, ex));
            cts.Cancel();
        }
    }, cts.Token);
}
