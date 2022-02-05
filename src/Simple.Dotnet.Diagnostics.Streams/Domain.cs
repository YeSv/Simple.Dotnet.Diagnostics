using Simple.Dotnet.Utilities.Results;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;

namespace Simple.Dotnet.Diagnostics.Streams;

// Stream implemented per connection type
public interface IStream
{
    ValueTask<UniResult<Unit, Exception>> Send(EventMetric metric, CancellationToken token);
    ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<EventMetric> metrics, CancellationToken token);
}