using Cysharp.Text;
using Microsoft.AspNetCore.Http;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.Streams.ServerSentEvents;

public enum SseResultType : byte { ClosedByClient, Failure };

public enum SseEventType : byte { Single, Batch }

public readonly record struct SseResult(SseResultType Type, Exception? Error);

public readonly record struct SseSubscription(Task<SseResult> Task);

public readonly record struct SseError(string Message);

public sealed class SseStream : IStream
{
    HttpResponse? _response;
    TaskCompletionSource<SseResult>? _tcs;

    readonly JsonSerializerOptions? _options;

    public SseStream(JsonSerializerOptions? options) => _options = options;

    public Result<SseSubscription, SseError> Subscribe(HttpResponse response)
    {
        if (response.HasStarted) return Result.Error<SseSubscription, SseError>(new("Response has already started. Can't subscribe"));

        _response = response;
        _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _response.ContentType = "text/event-stream";
        _response.Headers["Cache-Control"] = "no-cache";
        _response.HttpContext.RequestAborted.Register(tcs => ((TaskCompletionSource<SseResult>)tcs!).TrySetResult(new(SseResultType.ClosedByClient, null)), _tcs, false);

        return Result.Ok<SseSubscription, SseError>(new(_tcs.Task));
    }

    public ValueTask Send(StreamData data, CancellationToken token)
    {
        if (_response is null || _tcs is null) return ValueTask.CompletedTask;
        if (data.Data is null) return ValueTask.CompletedTask;

        return Send(Format(data, _options), token);
    }

    public ValueTask Send(ReadOnlyMemory<StreamData> batch, CancellationToken token)
    {
        if (batch.Length == 0) return ValueTask.CompletedTask;
        if (batch.Length == 1) return Send(batch.Span[0], token);

        return Send(Format(batch, _options), token);
    }

    async ValueTask Send(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        try
        {
            if (!_response!.HasStarted) await _response.StartAsync(token);
            await _response.BodyWriter.WriteAsync(bytes, token);
        }
        catch (Exception ex)
        {
            _tcs!.TrySetResult(new(SseResultType.Failure, ex));
        }
    }

    static ReadOnlyMemory<byte> Format(StreamData data, JsonSerializerOptions? options)
    {
        using var disposingData = data.Data;
        using var builder = new Utf8ValueStringBuilder(true);
        using var writerRent = BufferWriterPool<byte>.Shared.Get();

        JsonSerializer.Serialize(new Utf8JsonWriter(writerRent.Value), disposingData, options);

        builder.Append($"event: {nameof(SseEventType.Single)}\n");
        builder.Append("data: ");
        builder.Append(writerRent.Value.WrittenMemory);
        builder.Append('\n');
        builder.Append('\n');

        return builder.AsSpan().ToArray();
    }

    static ReadOnlyMemory<byte> Format(ReadOnlyMemory<StreamData> batch, JsonSerializerOptions? options)
    {
        if (batch.Length == 0) return Array.Empty<byte>();
        try
        {
            using var builder = new Utf8ValueStringBuilder(true);
            using var writerRent = BufferWriterPool<byte>.Shared.Get();
            using var poolableRent = new Rent<IPoolable>(batch.Length);

            var batchSpan = batch.Span;
            for (var i = 0; i < batchSpan.Length; i++)
            {
                if (batchSpan[i].Data != null) poolableRent.Append(batchSpan[i].Data);
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
            for (var i = 0; i < batch.Length; i++) batch.Span[i].Data?.Dispose();
        }
    }
}