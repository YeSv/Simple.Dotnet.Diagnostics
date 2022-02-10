using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;

public sealed class Subscription
{
    static readonly Task<UniResult<Unit, Exception>> UsedResult =
        Task.FromResult<UniResult<Unit, Exception>>(new(new Exception("This subscription can be used only once")));

    EventPipeSession? _session;

    public Subscription(EventPipeSession session) => _session = session;

    public Task<UniResult<Unit, Exception>> Start(Action<EventMetric> sideEffect, CancellationToken token)
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
        Action<EventMetric> sideEffect,
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
                    "Sum" => new((string)payload["Name"], (double)payload["Increment"], EventMetricType.Counter),
                    _ => new((string)payload["Name"], (double)payload["Mean"], EventMetricType.Gauge)
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
