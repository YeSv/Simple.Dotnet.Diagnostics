using System.Diagnostics.Tracing;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.Counters;

public readonly record struct CounterProvider(string Name, long Keywords, EventLevel Level);

public static class Registry
{
    public static readonly CounterProvider[] Providers = new CounterProvider[]
    {
        new("System.Runtime", 0xffffffff, EventLevel.Verbose),
        new("Microsoft.AspNetCore.Hosting", 0, EventLevel.Informational),
        new("Microsoft-AspNetCore-Server-Kestrel", 0, EventLevel.Informational),
        new("System.Net.Http", 0, EventLevel.Informational),
        new("System.Net.NameResolution", 0,EventLevel.Informational),
        new("System.Net.Security", 0, EventLevel.Informational),
        new("System.Net.Sockets", 0, EventLevel.Informational)
    };

    public static CounterProvider Default => Providers[0];

    public static CounterProvider? Find(string name)
    {
        // Array is small so no need for a dictionary here
        for (var i = 0; i < Providers.Length; i++)
        {
            if (Providers[i].Name == name) return Providers[i];
        }

        return default;
    }
}