using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Streams;

// Stream implemented per connection type
public interface IStream
{
    ValueTask<UniResult<Unit, Exception>> Send(CounterMetric metric, CancellationToken token);
    ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<CounterMetric> metrics, CancellationToken token);
}