using MongoDB.Bson;
using MongoDB.Driver;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using Microsoft.Extensions.Configuration;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;

namespace Simple.Dotnet.Diagnostics.Streams.Streams;

public sealed class MongoConfig
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Database { get; set; } = "measurements";
    public string Collection { get; set; } = "measurements";

    public string TimeStampField { get; set; } = "timestamp";
    public string MetadataField { get; set; } = "metadata";
    public string ValueField { get; set; } = "value";
}

public sealed class Mongo : IStream
{
    static readonly InsertManyOptions ManyOpts = new() { IsOrdered = false };

    readonly MongoConfig _config;
    readonly IMongoCollection<BsonDocument> _collection;

    public Mongo(IConfigurationSection configuration) : this(configuration.Get<MongoConfig>()) { }

    public Mongo(MongoConfig config)
    {
        _config = config;
        _collection = new MongoClient(config.ConnectionString).GetDatabase(config.Database).GetCollection<BsonDocument>(config.Collection);
    }

    public ValueTask<UniResult<Unit, Exception>> Send(EventMetric metric, CancellationToken token)
    {
        var formatResult = Format(metric, DateTime.UtcNow, _config);
        if (!formatResult.IsOk) return new(UniResult.Error<Unit, Exception>(formatResult.Error!));
        if (formatResult.Ok == null) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));

        return new(Send(formatResult.Ok!, token));
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<EventMetric> batch, CancellationToken token)
    {
        if (batch.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 1) return Send(batch.Span[0], token);

        var formatResult = Format(batch, DateTime.UtcNow, _config);
        if (!formatResult.IsOk) return new(UniResult.Error<Unit, Exception>(formatResult.Error!));

        return new(Send(formatResult.Ok!, token));
    }

    Task<UniResult<Unit, Exception>> Send(BsonDocument document, CancellationToken token) =>
        _collection.InsertOneAsync(document, null, token).ContinueWith(t => t switch
        {
            { IsFaulted: true, Exception: var error } => new(error!.InnerException!),
            _ => UniResult.Ok<Unit, Exception>(Unit.Shared)
        });

    Task<UniResult<Unit, Exception>> Send(BsonDocument[] documents, CancellationToken token) =>
        _collection.InsertManyAsync(documents, ManyOpts, token).ContinueWith(t => t switch
        {
            { IsFaulted: true, Exception: var error } => new(error!.InnerException!),
            _ => UniResult.Ok<Unit, Exception>(Unit.Shared)
        });

    static UniResult<BsonDocument?, Exception> Format(in EventMetric metric, DateTime timestamp, MongoConfig formattingConfig)
    {
        try
        {
            return new(ToDocument(metric, timestamp, formattingConfig));
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    static UniResult<BsonDocument[], Exception> Format(ReadOnlyMemory<EventMetric> data, DateTime timestamp, MongoConfig formattingConfig)
    {
        try
        {
            using var documents = new Rent<BsonDocument>(data.Length);
            for (var i = 0; i < data.Length; i++) documents.Append(ToDocument(data.Span[i], timestamp, formattingConfig));

            return new(documents.WrittenSpan.ToArray());
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    // TODO: Optimize serialization
    static BsonDocument ToDocument(in EventMetric metric, DateTime timestamp, MongoConfig formattingConfig) =>
        new BsonDocument
        {
            { formattingConfig.TimeStampField, timestamp },
            {
                formattingConfig.MetadataField, new BsonDocument
                {
                    { "type", metric.Name }
                }
            },
            { formattingConfig.ValueField, metric.Value }
        };
}
