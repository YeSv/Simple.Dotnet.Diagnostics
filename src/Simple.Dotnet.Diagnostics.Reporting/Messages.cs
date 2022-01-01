using Microsoft.Extensions.ObjectPool;
using Simple.Dotnet.Diagnostics.Core.Domains.EventPipes;

namespace Simple.Dotnet.Diagnostics.Streams;

public sealed class SendEventCommand : IDisposable
{
    public EventMetric Metric { get; private set; }

    public void Dispose()
    {
        Metric = default;
        Pool.Return(this);
    }

    public static SendEventCommand Create(in EventMetric metric)
    {
        var command = Pool.Get();
        command.Metric = metric;
        return command;
    }

    public static readonly ObjectPool<SendEventCommand> Pool = new DefaultObjectPool<SendEventCommand>(new DefaultPooledObjectPolicy<SendEventCommand>(), Environment.ProcessorCount * 2);
}
