using Microsoft.Diagnostics.NETCore.Client;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.Counters;

public readonly record struct Counter(string Name, string Description, string[] SupportedVersions);

public readonly record struct CountersFilter(HashSet<string> Counters, EventPipeProvider[] Providers);

public sealed record CounterProvider(string Name, string Description, long Keywords, EventLevel Level, Dictionary<string, Counter> Counters)
{
    public CounterProvider(string name, string description, long keywords, EventLevel level, Counter[] counters)
        : this(name, description, keywords, level, counters.ToDictionary(c => c.Name)) { }
}
