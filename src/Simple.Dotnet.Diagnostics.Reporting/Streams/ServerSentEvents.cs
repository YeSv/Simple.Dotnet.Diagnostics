using Cysharp.Text;
using Microsoft.AspNetCore.Http;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.Streams.Streams;

public enum SseResultType : byte { ClosedByClient, HttpError, UnhandledException };

public enum SseEventType : byte { Single, Batch }

public readonly record struct SseResult(SseResultType Type, Exception? Error);

public readonly record struct SseSubscription(Task<SseResult> Task);

public readonly record struct SseError(string Message);

public sealed class SseStream : IStream
{
    readonly HttpResponse _response;
    readonly JsonSerializerOptions? _options;
    readonly TaskCompletionSource<SseResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SseStream(HttpResponse response, JsonSerializerOptions? options)
    {
        _options = options;
        _response = response;
        _response.ContentType = "text/event-stream";
        _response.Headers["Cache-Control"] = "no-cache";
        _response.HttpContext.RequestAborted.Register(tcs => ((TaskCompletionSource<SseResult>)tcs!).TrySetResult(new(SseResultType.ClosedByClient, null)), _tcs, false);
    }

    public Task<SseResult> Completion => _tcs.Task;

    public ValueTask<UniResult<Unit, Exception>> Send(StreamData data, CancellationToken token) 
    {
        try
        {
            return data switch
            {
                { Data: null } => new(UniResult.Ok<Unit, Exception>(Unit.Shared)),
                var d => new(Send(Format(d, _options), token))
            };
        }
        catch (Exception ex)
        {
            _tcs.TrySetResult(new(SseResultType.UnhandledException, ex));
            return new(UniResult.Error<Unit, Exception>(ex));
        }
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<StreamData> batch, CancellationToken token)
    {
        if (batch.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 1) return Send(batch.Span[0], token);

        try
        {
            return new(Send(Format(batch, _options), token));
        }
        catch (Exception ex)
        {
            _tcs.TrySetResult(new(SseResultType.UnhandledException, ex));
            return new(UniResult.Error<Unit, Exception>(ex));
        }
    }

    async Task<UniResult<Unit, Exception>> Send(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        try
        {
            if (!_response!.HasStarted) await _response.StartAsync(token);
            await _response.BodyWriter.WriteAsync(bytes, token);

            return UniResult.Ok<Unit, Exception>(Unit.Shared);
        }
        catch (Exception ex)
        {
            _tcs!.TrySetResult(new(SseResultType.HttpError, ex));
            return UniResult.Error<Unit, Exception>(ex);
        }
    }

    static ReadOnlyMemory<byte> Format(StreamData data, JsonSerializerOptions? options)
    {
        try
        {
            using var builder = new Utf8ValueStringBuilder(true);
            using var writerRent = BufferWriterPool<byte>.Shared.Get();

            JsonSerializer.Serialize(new Utf8JsonWriter(writerRent.Value), data.Data, options);

            builder.Append($"event: {nameof(SseEventType.Single)}\n");
            builder.Append("data: ");
            builder.Append(writerRent.Value.WrittenMemory);
            builder.Append('\n');
            builder.Append('\n');

            return builder.AsSpan().ToArray();
        }
        finally
        {
            (data.Data as IDisposable)?.Dispose();
        }
    }

    static ReadOnlyMemory<byte> Format(ReadOnlyMemory<StreamData> batch, JsonSerializerOptions? options)
    {
        if (batch.Length == 0) return Array.Empty<byte>();
        try
        {
            using var builder = new Utf8ValueStringBuilder(true);
            using var writerRent = BufferWriterPool<byte>.Shared.Get();
            using var poolableRent = new Rent<object>(batch.Length);

            var batchSpan = batch.Span;
            for (var i = 0; i < batchSpan.Length; i++)
            {
                if (batchSpan[i].Data != null) poolableRent.Append(batchSpan[i].Data!);
            }

            JsonSerializer.Serialize(new Utf8JsonWriter(writerRent.Value), poolableRent.WrittenMemory, options);

            builder.Append($"event: {nameof(SseEventType.Batch)}\n");
            builder.Append("data: ");
            builder.Append(writerRent.Value.WrittenMemory);
            builder.Append('\n');
            builder.Append('\n');

            return builder.AsSpan().ToArray();
        }
        finally
        {
            for (var i = 0; i < batch.Length; i++) (batch.Span[i].Data as IDisposable)?.Dispose();
        }
    }
}