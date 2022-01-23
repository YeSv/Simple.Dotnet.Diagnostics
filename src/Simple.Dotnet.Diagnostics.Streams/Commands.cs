using Microsoft.Extensions.ObjectPool;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;
using Simple.Dotnet.Utilities.Pools;

namespace Simple.Dotnet.Diagnostics.Streams;

public sealed class SendEventCommand
{
    public EventMetric Metric { get; private set; }

    public static ValueRent<object> Rent(in EventMetric metric)
    {
        var cmd = Pool.Get();
        cmd.Metric = metric;

        return new(cmd, Pool, (c, p) =>
        {
            var (cmd, pool) = ((SendEventCommand)c, (ObjectPool<SendEventCommand>)p!);
            cmd.Metric = default;
            pool.Return(cmd);
        });
    }

    static readonly ObjectPool<SendEventCommand> Pool = new DefaultObjectPool<SendEventCommand>(new DefaultPooledObjectPolicy<SendEventCommand>(), Environment.ProcessorCount * 2);
}
