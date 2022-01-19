﻿using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;

public sealed class Subscription : IDisposable
{
    CancellationTokenSource? _cts;
    TaskCompletionSource<UniResult<Unit, Exception>>? _tcs;

    readonly EventPipeSession _session;
    readonly EventPipeEventSource _source;

    public Subscription(EventPipeSession session, EventPipeEventSource source)
    {
        _source = source;
        _session = session;
    }

    public Task<UniResult<Unit, Exception>> Start(Action<EventMetric> sideEffect, CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _cts.Token.Register(s => ((EventPipeSession)s!).Stop(), _session, false); // Stop session once canceled

        _source.Dynamic.All += e =>
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
                _tcs.TrySetResult(UniResult.Error<Unit, Exception>(ex));
            }
        };

        var processTask = Task.Factory.StartNew(s =>
        {
            var subscription = (Subscription)s!;
            try
            {
                subscription._source.Process();
                subscription._tcs!.TrySetResult(UniResult.Ok<Unit, Exception>(Unit.Shared));
            }
            catch (Exception ex)
            {
                subscription._tcs!.TrySetResult(UniResult.Error<Unit, Exception>(ex));
            }
        }, this, _cts.Token);


        return Task.WhenAll(processTask, _tcs.Task).ContinueWith((t, s) =>
        {
            using var subscription = (Subscription)s!;
            return subscription._tcs!.Task.Result;
        }, this);
    }

    void IDisposable.Dispose()
    {
        _source.Dispose();
        _session.Dispose();
    }
}
