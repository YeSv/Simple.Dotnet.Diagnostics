﻿using Confluent.Kafka;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.Streams.Streams;

public sealed class KafkaConfig
{
    public string Topic { get; set; } = string.Empty;
    public Dictionary<string, string> ProducerConfig { get; set; } = new();
}

public sealed class Kafka : IStream, IDisposable
{
    private readonly KafkaConfig _config;
    private readonly IProducer<byte[], byte[]> _producer;

    public Kafka(KafkaConfig config)
    {
        _config = config;
        _producer = new ProducerBuilder<byte[], byte[]>(config.ProducerConfig).Build();
    }

    public ValueTask<UniResult<Unit, Exception>> Send(StreamData data, CancellationToken token)
    {
        var formatResult = Format(data);
        if (!formatResult.IsOk) return new(UniResult.Error<Unit, Exception>(formatResult.Error!));
        if (formatResult.Ok == null) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));

        return new(Send(formatResult.Ok!, _config.Topic, token));
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<StreamData> batch, CancellationToken token)
    {
        if (batch.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 1) return Send(batch.Span[0], token);

        var formatResult = Format(batch);
        if (!formatResult.IsOk) return new(UniResult.Error<Unit, Exception>(formatResult.Error!));

        return new(Send(formatResult.Ok!, _config.Topic, token));
    }

    public void Dispose() => _producer?.Dispose();

    Task<UniResult<Unit, Exception>> Send(byte[] data, string topic, CancellationToken token) =>
        _producer.ProduceAsync(topic, new()
        {
            Key = Array.Empty<byte>(),
            Value = data
        }, token).ContinueWith(t => t switch
        {
            { IsFaulted: true, Exception: var e } => UniResult.Error<Unit, Exception>(e!.InnerException!),
            _ => UniResult.Ok<Unit, Exception>(Unit.Shared)
        });

    Task<UniResult<Unit, Exception>> Send(ReadOnlyMemory<byte[]> batch, string topic, CancellationToken token)
    {
        try
        {
            for (var i = 0; i < batch.Length - 1; i++) _producer.Produce(topic, new()
            {
                Key = Array.Empty<byte>(),
                Value = batch.Span[i]
            });

            return Send(batch.Span[^1], topic, token);
        }
        catch (Exception ex)
        {
            return Task.FromResult(UniResult.Error<Unit, Exception>(ex));
        }
    }

    static UniResult<byte[]?, Exception> Format(in StreamData data)
    {
        try
        {
            using var valueRent = data.Rent;
            if (valueRent.Value is not SendEventCommand cmd) return new((byte[]?)null);

            using var writerRent = BufferWriterPool<byte>.Shared.Get();
            JsonSerializer.Serialize(new Utf8JsonWriter(writerRent.Value), cmd.Metric);

            return new(writerRent.Value.WrittenSpan.ToArray());
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    static Result<ReadOnlyMemory<byte[]>, Exception> Format(ReadOnlyMemory<StreamData> batch)
    {
        try
        {
            using var formatted = new Rent<byte[]>(batch.Length);

            for (var i = 0; i < batch.Length; i++)
            {
                if (batch.Span[i].Rent.Value is not SendEventCommand cmd) continue;

                using var writer = BufferWriterPool<byte>.Shared.Get();
                JsonSerializer.Serialize(new Utf8JsonWriter(writer.Value), cmd.Metric);
            }

            return new(formatted.WrittenSpan.ToArray());
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