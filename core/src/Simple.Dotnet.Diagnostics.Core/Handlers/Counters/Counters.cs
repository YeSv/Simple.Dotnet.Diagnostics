using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System.Runtime.CompilerServices;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.Counters;

// TODO: Add support for Provider[Counter1, Counter2], Provider, Provider.... queries
public readonly record struct CountersQuery(int? ProcessId, string? Providers, string? ProcessName, uint? RefreshInterval);

public sealed class Counters
{
    public static Result<CountersSubscription, DiagnosticsError> Handle(ref CountersQuery query, CancellationToken token)
    {
        if (query.ProcessId is < 0)
            return Result.Error<CountersSubscription, DiagnosticsError>(new("Query contains not valid process id"));

        if (!query.ProcessId.HasValue && string.IsNullOrWhiteSpace(query.ProcessName))
            return Result.Error<CountersSubscription, DiagnosticsError>(new("Process name or process id should be specified"));

        if (query.RefreshInterval is <= 0)
            return Result.Error<CountersSubscription, DiagnosticsError>(new("Refresh interval in seconds can't be less than or equal to 0"));

        if (!string.IsNullOrWhiteSpace(query.ProcessName))
        {
            var result = Processes.Handle(new GetProcessByNameQuery(query.ProcessName!), token);
            if (!result.IsOk) return Result.Error<CountersSubscription, DiagnosticsError>(result.Error);

            query = query with { ProcessId = result.Ok.ProcessId };
        }

        var idLookup = Processes.Handle(new GetProcessByIdQuery(query.ProcessId!.Value), token);
        if (!idLookup.IsOk) return Result.Error<CountersSubscription, DiagnosticsError>(idLookup.Error);

        var parsedProviders = ParseProviders(query.Providers);
        if (!parsedProviders.IsOk) return new(new DiagnosticsError("Failed to parse providers"));

        var providerArguments = new Dictionary<string, string>
        {
            ["EventCounterIntervalSec"] = (query.RefreshInterval ?? 10).ToString()
        };

        var session = GetSession(query.ProcessId!.Value, parsedProviders.Ok!, providerArguments, token);
        if (!session.IsOk) return new(session.Error);

        return new(new CountersSubscription(session.Ok!));
    }

    static Result<EventPipeSession, DiagnosticsError> GetSession(
        int processId,
        string[]? parsedProviders, 
        Dictionary<string, string> providerArguments, 
        CancellationToken token)
    {
        if (parsedProviders is null or { Length: 0 }) return EventPipes.Handle(new(processId,
            new EventPipeProvider[]
            {
                new EventPipeProvider(Registry.Default.Name, Registry.Default.Level, Registry.Default.Keywords, providerArguments)
            }),
            token);

        try
        {
            using var providers = new Rent<EventPipeProvider>(parsedProviders.Length);
            foreach (var providerName in parsedProviders)
            {
                var provider = Registry.Find(providerName);
                if (provider is null) return Result.Error<EventPipeSession, DiagnosticsError>(new($"Unknown provider name: '{providerName}'"));

                providers.Append(new(provider!.Value.Name, provider.Value.Level, provider.Value.Keywords, providerArguments));
            }

            return EventPipes.Handle(new(processId, providers.WrittenSpan.ToArray()), token);
        }
        catch (Exception ex)
        {
            return Result.Error<EventPipeSession, DiagnosticsError>(new(ex));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static UniResult<string[], Exception> ParseProviders(string? providers)
    {
        if (string.IsNullOrWhiteSpace(providers)) return new(Array.Empty<string>());
        return new(providers.Trim().Split(','));
    }
}


