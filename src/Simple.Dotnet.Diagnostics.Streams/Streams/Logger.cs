using Microsoft.Extensions.Logging;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Streams.Streams;

public sealed class LoggerStream : IStream
{
    readonly ILogger _logger;

    public LoggerStream(ILogger logger) => _logger = logger;

    public ValueTask<UniResult<Unit, Exception>> Send(EventMetric metric, CancellationToken token)
    {
        _logger.LogInformation("Received metric: {MetricName}[{MetricType}] = {MetricValue}", metric.Name, metric.Type, metric.Value);
        return new(UniResult.Ok<Unit, Exception>(Unit.Shared));
    }

    public ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<EventMetric> metrics, CancellationToken token)
    {
        for (var i = 0; i < metrics.Length; i++) _ = Send(metrics.Span[i], token);
        return new(UniResult.Ok<Unit, Exception>(Unit.Shared)); 
    }
}
