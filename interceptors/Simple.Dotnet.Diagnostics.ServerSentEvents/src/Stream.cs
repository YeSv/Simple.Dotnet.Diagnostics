using Cysharp.Text;
using Microsoft.AspNetCore.Http;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;
using Simple.Dotnet.Diagnostics.Streams;
using Simple.Dotnet.Utilities.Results;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.ServerSentEvents;

public enum SseEventType : byte { Single, Batch }

public sealed class SseStream : IStream
{
    readonly HttpResponse _response;
    readonly JsonSerializerOptions? _options;

    public SseStream(HttpResponse response, JsonSerializerOptions? options)
    {
        _options = options;
        _response = response;
        _response.ContentType = "text/event-stream";
        _response.Headers["Cache-Control"] = "no-cache";
    }

    public ValueTask<UniResult<Unit, Exception>> Send(EventMetric metric, CancellationToken token)
    {
        var formatResult = Format(metric, _options);
        if (formatResult.IsOk) return new(Send(formatResult.Ok, token));

        // Failed to format
        return new(UniResult.Error<Unit, Exception>(formatResult.Error!));
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<EventMetric> batch, CancellationToken token)
    {
        if (batch.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 1) return Send(batch.Span[0], token);

        var formatResult = Format(batch, _options);
        if (formatResult.IsOk) return new(Send(formatResult.Ok, token));

        // Failed to format values
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
        catch (OperationCanceledException)
        {
            return new(Unit.Shared);
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    // Serializes commands
    static Result<ReadOnlyMemory<byte>, Exception> Format(in EventMetric metric, JsonSerializerOptions? options)
    {
        try
        {
            using var builder = new Utf8ValueStringBuilder(true);

            builder.Append($"event: {nameof(SseEventType.Single)}\n");
            builder.Append("data: ");
            builder.Append(JsonSerializer.Serialize(metric, options));
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
    static Result<ReadOnlyMemory<byte>, Exception> Format(ReadOnlyMemory<EventMetric> batch, JsonSerializerOptions? options)
    {
        try
        {
            using var builder = new Utf8ValueStringBuilder(true);

            builder.Append($"event: {nameof(SseEventType.Batch)}\n");
            builder.Append("data: ");
            builder.Append(JsonSerializer.Serialize(batch.ToArray(), options)); // Alloc...
            builder.Append('\n');
            builder.Append('\n');

            return new(builder.AsSpan().ToArray());
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }
}