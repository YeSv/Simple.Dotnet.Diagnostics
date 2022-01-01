using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Diagnostics.Core.Domains.Counters;
using Simple.Dotnet.Diagnostics.Core.Domains.EventPipes;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Simple.Dotnet.Diagnostics.Core.Handlers;

// TODO: Add support for Provider[Counter1, Counter2], Provider, Provider.... queries
public readonly record struct CountersQuery(int? ProcessId, string[]? Providers, string? ProcessName, uint? RefreshInterval);

public static class Counters
{
    public static Result<EventsSubscription, DiagnosticsError> Handle(ref CountersQuery query, CancellationToken token)
    {
        if (query.ProcessId is < 0) return Result.Error<EventsSubscription, DiagnosticsError>(new("Query contains not valid process id"));
        if (!query.ProcessId.HasValue && string.IsNullOrWhiteSpace(query.ProcessName)) return Result.Error<EventsSubscription, DiagnosticsError>(new("Process name or process id should be specified"));
        if (query.RefreshInterval is <= 0) return Result.Error<EventsSubscription, DiagnosticsError>(new("Refresh interval in seconds can't be less than or equal to 0"));

        if (!string.IsNullOrWhiteSpace(query.ProcessName))
        {
            var result = Processes.Handle(new GetProcessByNameQuery(query.ProcessName!), token);
            if (!result.IsOk) return Result.Error<EventsSubscription, DiagnosticsError>(result.Error);
            query = query with { ProcessId = result.Ok.ProcessId };
        }

        var idLookup = Processes.Handle(new GetProcessByIdQuery(query.ProcessId!.Value), token);
        if (!idLookup.IsOk) return Result.Error<EventsSubscription, DiagnosticsError>(idLookup.Error);

        var providerArguments = query.RefreshInterval == null ? null : new Dictionary<string, string>
        {
            ["EventCounterIntervalSec"] = query.RefreshInterval!.Value.ToString()
        };

        if (query.Providers is null or { Length: 0 }) return EventPipes.Handle(new(query.ProcessId!.Value,
            new EventPipeProvider[]
            {
                new (CounterProvidersRegistry.DefaultName, CounterProvidersRegistry.Default.Level, CounterProvidersRegistry.Default.Keywords, providerArguments)
            }),
            token);

        try
        {
            using var rent = new Rent<EventPipeProvider>(query.Providers!.Length);
            foreach (var providerName in query.Providers)
            {
                if (!CounterProvidersRegistry.KnownProviders.TryGetValue(providerName, out var provider)) return Result.Error<EventsSubscription, DiagnosticsError>(new($"Unknown provider name: '{providerName}'"));
                rent.Append(new(provider.Name, provider.Level, provider.Keywords, providerArguments));
            }

            return EventPipes.Handle(new(query.ProcessId!.Value, rent.WrittenSpan.ToArray()), token);
        }
        catch (Exception ex)
        {
            return Result.Error<EventsSubscription, DiagnosticsError>(new(ex));
        }
    }
}


