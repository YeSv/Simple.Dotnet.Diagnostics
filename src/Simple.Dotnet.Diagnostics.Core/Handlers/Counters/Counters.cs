using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.Counters;

// TODO: Add support for Provider[Counter1, Counter2], Provider, Provider.... queries
public readonly record struct CountersQuery(int? ProcessId, string? Providers, string? ProcessName, uint? RefreshInterval);

public sealed class Counters
{
    public static Result<Subscription, DiagnosticsError> Handle(ref CountersQuery query, CancellationToken token)
    {
        if (query.ProcessId is < 0)
            return Result.Error<Subscription, DiagnosticsError>(new("Query contains not valid process id"));

        if (!query.ProcessId.HasValue && string.IsNullOrWhiteSpace(query.ProcessName))
            return Result.Error<Subscription, DiagnosticsError>(new("Process name or process id should be specified"));

        if (query.RefreshInterval is <= 0)
            return Result.Error<Subscription, DiagnosticsError>(new("Refresh interval in seconds can't be less than or equal to 0"));

        if (!string.IsNullOrWhiteSpace(query.ProcessName))
        {
            var result = Processes.Handle(new GetProcessByNameQuery(query.ProcessName!), token);
            if (!result.IsOk) return Result.Error<Subscription, DiagnosticsError>(result.Error);

            query = query with { ProcessId = result.Ok.ProcessId };
        }

        var idLookup = Processes.Handle(new GetProcessByIdQuery(query.ProcessId!.Value), token);
        if (!idLookup.IsOk) return Result.Error<Subscription, DiagnosticsError>(idLookup.Error);

        var parsedProviders = ParseProviders(query.Providers);
        if (!parsedProviders.IsOk) return new(new DiagnosticsError("Failed to parse providers"));

        var providerArguments = query.RefreshInterval == null ? null : new Dictionary<string, string>
        {
            ["EventCounterIntervalSec"] = query.RefreshInterval!.Value.ToString()
        };

        if (query.Providers is null or { Length: 0 }) return EventPipes.EventPipes.Handle(new(query.ProcessId!.Value,
            new EventPipeProvider[]
            {
                new (Registry.DefaultName, Registry.Default.Level, Registry.Default.Keywords, providerArguments)
            }),
            token);

        try
        {
            using var providers = new Rent<EventPipeProvider>(query.Providers!.Length);
            foreach (var providerName in parsedProviders.Ok!)
            {
                if (!Registry.KnownProviders.TryGetValue(providerName, out var provider)) 
                    return Result.Error<Subscription, DiagnosticsError>(new($"Unknown provider name: '{providerName}'"));

                providers.Append(new(provider.Name, provider.Level, provider.Keywords, providerArguments));
            }

            return EventPipes.EventPipes.Handle(new(query.ProcessId!.Value, providers.WrittenSpan.ToArray()), token);
        }
        catch (Exception ex)
        {
            return Result.Error<Subscription, DiagnosticsError>(new(ex));
        }
    }

    static UniResult<string[], Exception> ParseProviders(string? providers)
    {
        if (string.IsNullOrWhiteSpace(providers)) return new(Array.Empty<string>());
        return new(providers.Trim().Split(','));
    }
}


