using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Collections.Generic;
using System.Threading;

using SubscriptionTask = System.Threading.Tasks.Task<Simple.Dotnet.Utilities.Results.UniResult<Simple.Dotnet.Utilities.Results.Unit, System.Exception>>;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.Counters;

// TODO: Add support for Provider[Counter1, Counter2], Provider, Provider.... queries
public readonly record struct CountersQuery(int? ProcessId, string[]? Providers, string? ProcessName, uint? RefreshInterval);

public sealed class Counters
{
    public static Result<SubscriptionTask, DiagnosticsError> Handle(ref CountersQuery query, Action<EventMetric> sideEffect, CancellationToken token)
    {
        if (query.ProcessId is < 0)
            return Result.Error<SubscriptionTask, DiagnosticsError>(new("Query contains not valid process id"));

        if (!query.ProcessId.HasValue && string.IsNullOrWhiteSpace(query.ProcessName))
            return Result.Error<SubscriptionTask, DiagnosticsError>(new("Process name or process id should be specified"));

        if (query.RefreshInterval is <= 0)
            return Result.Error<SubscriptionTask, DiagnosticsError>(new("Refresh interval in seconds can't be less than or equal to 0"));

        if (!string.IsNullOrWhiteSpace(query.ProcessName))
        {
            var result = Processes.Handle(new GetProcessByNameQuery(query.ProcessName!), token);
            if (!result.IsOk) return Result.Error<SubscriptionTask, DiagnosticsError>(result.Error);

            query = query with { ProcessId = result.Ok.ProcessId };
        }

        var idLookup = Processes.Handle(new GetProcessByIdQuery(query.ProcessId!.Value), token);
        if (!idLookup.IsOk) return Result.Error<SubscriptionTask, DiagnosticsError>(idLookup.Error);

        var providerArguments = query.RefreshInterval == null ? null : new Dictionary<string, string>
        {
            ["EventCounterIntervalSec"] = query.RefreshInterval!.Value.ToString()
        };

        if (query.Providers is null or { Length: 0 }) return EventPipes.EventPipes.Handle(new(query.ProcessId!.Value,
            new EventPipeProvider[]
            {
                new (Registry.DefaultName, Registry.Default.Level, Registry.Default.Keywords, providerArguments)
            }),
            sideEffect,
            token);

        try
        {
            using var rent = new Rent<EventPipeProvider>(query.Providers!.Length);
            foreach (var providerName in query.Providers)
            {
                if (!Registry.KnownProviders.TryGetValue(providerName, out var provider)) return Result.Error<SubscriptionTask, DiagnosticsError>(new($"Unknown provider name: '{providerName}'"));
                rent.Append(new(provider.Name, provider.Level, provider.Keywords, providerArguments));
            }

            return EventPipes.EventPipes.Handle(new(query.ProcessId!.Value, rent.WrittenSpan.ToArray()), sideEffect, token);
        }
        catch (Exception ex)
        {
            return Result.Error<SubscriptionTask, DiagnosticsError>(new(ex));
        }
    }
}


