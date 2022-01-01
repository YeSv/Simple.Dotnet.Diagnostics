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

    public Task<WebSocketResult> Completion => Task.WhenAll(_reader, _tcs.Task).ContinueWith((_, t) => ((Task<WebSocketResult>)t!).Result, _tcs.Task);

    public ValueTask<UniResult<Unit, Exception>> Send(StreamData data, CancellationToken token)
    {
        if (_ws is null || data.Data is null) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        try
        {
            using var writerRent = BufferWriterPool<byte>.Shared.Get();

            JsonSerializer.Serialize(new Utf8JsonWriter(writerRent.Value), data.Data, _jsonOptions);
            return new(Send(writerRent.Value.WrittenSpan.ToArray(), token));
        }
        catch (Exception ex)
        {
            _tcs?.TrySetResult(new(WebSocketResultType.UnhandledException, null, ex)); // Resolve completion as error
            return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        }
        finally
        {
            (data.Data as IDisposable)?.Dispose();
        }
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<StreamData> batch, CancellationToken token)
    {
        if (_ws is null) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 1) return Send(batch.Span[0], token);

        try
        {
            using var writerRent = BufferWriterPool<byte>.Shared.Get();
            using var poolableRent = new Rent<object>(batch.Length);

            for (var i = 0; i < batch.Length; i++)
            {
                if (batch.Span[i].Data != null) poolableRent.Append(batch.Span[i].Data!);
            }

            JsonSerializer.Serialize(new Utf8JsonWriter(writerRent.Value), poolableRent.WrittenMemory, _jsonOptions);

            return new(Send(writerRent.Value.WrittenSpan.ToArray(), token));
        }
        catch (Exception ex)
        {
            _tcs?.TrySetResult(new(WebSocketResultType.UnhandledException, null, ex)); // Resolve completion as error
            return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        }
        finally
        {
            for (var i = 0; i < batch.Length; i++) (batch.Span[i].Data as IDisposable)?.Dispose();
        }
    }

    async Task<UniResult<Unit, Exception>> Send(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        try
        {
            await _ws!.SendAsync(bytes, WebSocketMessageType.Binary, true, token);
        }
        catch (OperationCanceledException) {}
        catch (WebSocketException ex)
        {
            _tcs!.TrySetResult(new(WebSocketResultType.WsException, ex.WebSocketErrorCode, ex));
        }

        // Handle all exceptions and resolve Completion task as WsException, Ok otherwise
        return UniResult.Ok<Unit, Exception>(Unit.Shared);
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
        catch (OperationCanceledException) { }
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
