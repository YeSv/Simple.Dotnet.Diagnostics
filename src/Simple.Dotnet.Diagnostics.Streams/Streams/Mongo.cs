using MongoDB.Bson;
using MongoDB.Driver;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using Microsoft.Extensions.Configuration;

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

    public ValueTask<UniResult<Unit, Exception>> Send(StreamData data, CancellationToken token)
    {
        var document = Format(data, DateTime.UtcNow, _config);
        if (document == null) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));

        return new(Send(document, token));
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<StreamData> batch, CancellationToken token)
    {
        if (batch.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
        if (batch.Length == 1) return Send(batch.Span[0], token);

        var documents = Format(batch, DateTime.UtcNow, _config);
        if (documents.Length == 0) return new(UniResult.Ok<Unit, Exception>(Unit.Shared));

        return new(Send(documents, token));
    }

    static BsonDocument[] Format(ReadOnlyMemory<StreamData> batch, DateTime timestamp, MongoConfig formattingConfig)
    {
        using var documents = new Rent<BsonDocument>(batch.Length);

        for (var i = 0; i < batch.Length; i++)
        {
            var document = Format(batch.Span[i], timestamp, formattingConfig);
            if (document != null) documents.Append(document);
        }

        return documents.Written switch
        {
            0 => Array.Empty<BsonDocument>(),
            _ => documents.WrittenSpan.ToArray()
        };
    }

    // TODO: Optimize serialization
    static BsonDocument? Format(in StreamData data, DateTime timestamp, MongoConfig formattingConfig)
    {
        if (data.Data is not SendEventCommand cmd) return null;
        
        return new BsonDocument
        {
            { formattingConfig.TimeStampField, timestamp },
            {
                formattingConfig.MetadataField, new BsonDocument
                {
                    { "type", cmd.Metric.Name }
                }
            },
            { formattingConfig.ValueField, cmd.Metric.Value }
        };
    }

    Task<UniResult<Unit, Exception>> Send(BsonDocument document, CancellationToken token) =>
        _collection.InsertOneAsync(document, null, token).ContinueWith(t => t switch
        {
            { IsFaulted: true, Exception: var error } => UniResult.Error<Unit, Exception>(error!.InnerException!),
            _ => UniResult.Ok<Unit, Exception>(Unit.Shared)
        });

    Task<UniResult<Unit, Exception>> Send(BsonDocument[] documents, CancellationToken token) =>
        _collection.InsertManyAsync(documents, ManyOpts, token).ContinueWith(t => t switch
        {
            { IsFaulted: true, Exception: var error } => UniResult.Error<Unit, Exception>(error!.InnerException!),
            _ => UniResult.Ok<Unit, Exception>(Unit.Shared)
        });
}
