using Simple.Dotnet.Utilities.Pools;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Streams;

// Send data to a stream
public readonly record struct StreamData(ValueRent<object?> Rent, Guid SubscriptionId);

// Stream implemented per connection type
public interface IStream
{
    ValueTask<UniResult<Unit, Exception>> Send(StreamData data, CancellationToken token);
    ValueTask<UniResult<Unit, Exception>> Send(ReadOnlyMemory<StreamData> batch, CancellationToken token);
}
