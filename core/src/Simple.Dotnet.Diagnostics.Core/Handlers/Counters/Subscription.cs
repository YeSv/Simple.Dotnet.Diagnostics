using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.Counters;

public enum CounterMetricType : byte { Counter, Gauge }

public readonly record struct CounterMetric(string Name, double Value, CounterMetricType Type, IDictionary<string, object> Payload);


public sealed class CountersSubscription
{
    static readonly Task<UniResult<Unit, Exception>> UsedResult =
        Task.FromResult<UniResult<Unit, Exception>>(new(new Exception("This subscription can be used only once")));

    EventPipeSession? _session;

    public CountersSubscription(EventPipeSession session) => _session = session;

    public Task<UniResult<Unit, Exception>> Start(Action<CounterMetric> sideEffect, CancellationToken token)
    {
        if (_session == null) return UsedResult;

        (var session, _session) = (_session, null);
        var tcs = new TaskCompletionSource<UniResult<Unit, Exception>>(TaskCreationOptions.RunContinuationsAsynchronously);

        token.Register(s => ((EventPipeSession)s!).Stop(), session, false); // Stop session once canceled

        var processTask = Task.Factory.StartNew(() =>
        {
            using (var source = CreateSource(session, sideEffect, tcs))
            using (session)
            {
                try
                {
                    source.Process();
                    tcs!.TrySetResult(UniResult.Ok<Unit, Exception>(Unit.Shared));
                }
                catch (Exception ex)
                {
                    tcs!.TrySetResult(UniResult.Error<Unit, Exception>(ex));
                }
            }
        }, token);

        return Task.WhenAll(processTask, tcs.Task).ContinueWith(t => tcs.Task.Result);
    }

    EventPipeEventSource CreateSource(
        EventPipeSession session, 
        Action<CounterMetric> sideEffect,
        TaskCompletionSource<UniResult<Unit, Exception>> tcs)
    {
        var source = new EventPipeEventSource(session.EventStream);
        source.Dynamic.All += e =>
        {
            if (e.EventName != "EventCounters") return;
            try
            {
                var payload = (IDictionary<string, object>)((IDictionary<string, object>)e.PayloadValue(0))["Payload"];

                sideEffect((string)payload["CounterType"] switch
                {
                    "Sum" => new((string)payload["Name"], (double)payload["Increment"], CounterMetricType.Counter, payload),
                    _ => new((string)payload["Name"], (double)payload["Mean"], CounterMetricType.Gauge, payload)
                });
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(UniResult.Error<Unit, Exception>(ex));
            }
        };
        return source;
    }
}
