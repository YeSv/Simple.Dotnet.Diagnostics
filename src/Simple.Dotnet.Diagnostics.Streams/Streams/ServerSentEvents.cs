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
        if (data.Rent.Value == null) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));

        var formatResult = Format(data, _options);
        if (formatResult.IsOk) return new(Send(formatResult.Ok, token));

        // Failed to format
        _tcs.TrySetResult(new(SseResultType.UnhandledException, formatResult.Error!));
        return new(UniResult.Error<Unit, Exception>(formatResult.Error!));
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<StreamData> batch, CancellationToken token)
    {
        if (batch.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 1) return Send(batch.Span[0], token);

        var formatResult = Format(batch, _options);
        if (formatResult.IsOk) return new(Send(formatResult.Ok, token));

        // Failed to format values
        _tcs.TrySetResult(new(SseResultType.UnhandledException, formatResult.Error!));
        return new(UniResult.Error<Unit, Exception>(formatResult.Error!));
    }

    async Task<UniResult<Unit, Exception>> Send(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        try
        {
            if (!_response.HasStarted) await _response.StartAsync(token);
            await _response.BodyWriter.WriteAsync(bytes, token);

            return new(Unit.Shared);
        }
        catch (OperationCanceledException ex)
        {
            _tcs.TrySetResult(new(SseResultType.ClosedByClient, ex));
            return new(Unit.Shared);
        }
        catch (Exception ex)
        {
            _tcs.TrySetResult(new(SseResultType.HttpError, ex));
            return new(ex);
        }
    }

    // Serializes commands
    static Result<ReadOnlyMemory<byte>, Exception> Format(in StreamData data, JsonSerializerOptions? options)
    {
        try
        {
            using var valueRent = data.Rent;
            using var builder = new Utf8ValueStringBuilder(true);
            using var writer = BufferWriterPool<byte>.Shared.Get();

            JsonSerializer.Serialize(new Utf8JsonWriter(writer.Value), valueRent.Value, options);

            builder.Append($"event: {nameof(SseEventType.Single)}\n");
            builder.Append("data: ");
            builder.Append(writer.Value.WrittenMemory);
            builder.Append('\n');
            builder.Append('\n');

            return new(builder.AsSpan().ToArray());
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    // Serializes commands
    static Result<ReadOnlyMemory<byte>, Exception> Format(ReadOnlyMemory<StreamData> batch, JsonSerializerOptions? options)
    {
        try
        {
            using var builder = new Utf8ValueStringBuilder(true);
            using var writer = BufferWriterPool<byte>.Shared.Get();
            using var poolables = new Rent<object>(batch.Length);

            var batchSpan = batch.Span;
            for (var i = 0; i < batchSpan.Length; i++)
            {
                if (batchSpan[i].Rent.Value != null) poolables.Append(batchSpan[i].Rent.Value!);
            }

            JsonSerializer.Serialize(new Utf8JsonWriter(writer.Value), poolables.WrittenMemory, options);

            builder.Append($"event: {nameof(SseEventType.Batch)}\n");
            builder.Append("data: ");
            builder.Append(writer.Value.WrittenMemory);
            builder.Append('\n');
            builder.Append('\n');

            return new(builder.AsSpan().ToArray());
        }
        catch (Exception ex)
        {
            return new(ex);
        }
        finally
        {
            for (var i = 0; i < batch.Length; i++) batch.Span[i].Rent.Dispose();
        }
    }
}