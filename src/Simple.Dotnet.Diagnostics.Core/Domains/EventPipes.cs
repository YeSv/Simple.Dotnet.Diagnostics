using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Simple.Dotnet.Diagnostics.Core.Domains.EventPipes;

public enum EventMetricType : byte { Counter, Gauge }

public readonly record struct EventMetric(string Name, double Value, EventMetricType Type);

public sealed class EventsSubscription : IDisposable
{
    static readonly Task<Exception?> Succeeded = Task.FromResult((Exception?)null);

    readonly EventPipeSession _session;
    readonly EventPipeEventSource _source;

    public EventsSubscription(EventPipeSession session, EventPipeEventSource source) => (_session, _source) = (session, source);

    public Action<EventMetric>? OnMetric;

    public Task<Exception?> Start(CancellationToken token)
    {
        if (OnMetric == null) return Succeeded;
        if (token.CanBeCanceled) token.Register(s => ((EventPipeSession)s).Stop(), _session, false);

        _source.Dynamic.All += e =>
        {
            if (e.EventName != "EventCounters") return;

            var payload = (IDictionary<string, object>)((IDictionary<string, object>)e.PayloadValue(0))["Payload"];

            OnMetric((string)payload["CounterType"] switch
            {
                "Sum" => new((string)payload["Name"], (double)payload["Increment"], EventMetricType.Counter),
                _ => new((string)payload["Name"], (double)payload["Mean"], EventMetricType.Gauge)
            });
        };

        return Task.Factory.StartNew(s =>
        {
            try
            {
                ((EventPipeEventSource)s!).Process();
                return default;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }, _source, token);
    }

    public void Dispose()
    {
        _source?.Dispose();
        _session?.Dispose();
    }
}
