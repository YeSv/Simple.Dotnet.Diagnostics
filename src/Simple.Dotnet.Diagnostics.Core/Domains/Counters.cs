using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

namespace Simple.Dotnet.Diagnostics.Core.Domains.Counters;

public readonly record struct Counter(string Name, string Description, string[] SupportedVersions);

public readonly record struct CountersFilter(HashSet<string> Counters, EventPipeProvider[] Providers);

public sealed record CounterProvider(string Name, string Description, long Keywords, EventLevel Level, Dictionary<string, Counter> Counters)
{
    public CounterProvider(string name, string description, long keywords, EventLevel level, Counter[] counters)
        : this(name, description, keywords, level, counters.ToDictionary(c => c.Name)) { }
}

public static class CounterProvidersRegistry
{
    static readonly string net60 = "6.0";
    static readonly string net50 = "5.0";
    static readonly string net31 = "3.1";
    static readonly string net30 = "3.0";

    public static readonly string DefaultName = "System.Runtime";
    public static readonly Dictionary<string, CounterProvider> KnownProviders = GetAvailableProviders().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public static CounterProvider Default => KnownProviders[DefaultName];

    static CounterProvider[] GetAvailableProviders() => new CounterProvider[]
    {
        new(
            "System.Runtime", // Name
            "A default set of performance counters provided by the .NET runtime.", // Description
            0xffffffff, // Keywords
            EventLevel.Verbose, // Level 
            new Counter[] { // Counters
                new("cpu-usage", "The percent of process' CPU usage relative to all of the system CPU resources [0-100]", new[] { net30, net31, net50 }),
                new("working-set", "Amount of working set used by the process (MB)", new[] { net30, net31, net50 }),
                new("gc-heap-size", "Total heap size reported by the GC (MB)", new[] { net30, net31, net50 }),
                new("gen-0-gc-count", "Number of Gen 0 GCs between update intervals", new[] { net30, net31, net50 }),
                new("gen-1-gc-count", "Number of Gen 1 GCs between update intervals", new[] { net30, net31, net50 }),
                new("gen-2-gc-count", "Number of Gen 2 GCs between update intervals", new[] { net30, net31, net50 }),
                new("time-in-gc", "% time in GC since the last GC", new[] { net30, net31, net50 }),
                new("gen-0-size", "Gen 0 Heap Size", new[] { net30, net31, net50 }),
                new("gen-1-size", "Gen 1 Heap Size", new[] { net30, net31, net50 }),
                new("gen-2-size", "Gen 2 Heap Size", new[] { net30, net31, net50 }),
                new("loh-size", "LOH Size", new[] { net30, net31, net50 }),
                new("poh-size", "POH (Pinned Object Heap) Size", new[] { net50 }),
                new("alloc-rate", "Number of bytes allocated in the managed heap between update intervals", new[] { net30, net31, net50 }),
                new("gc-fragmentation", "GC Heap Fragmentation", new[] { net50 }),
                new("assembly-count", "Number of Assemblies Loaded", new[] { net30, net31, net50 }),
                new("exception-count", "Number of Exceptions / sec", new[] { net30, net31, net50 }),
                new("threadpool-thread-count", "Number of ThreadPool Threads", new[] { net30, net31, net50 }),
                new("monitor-lock-contention-count", "Number of times there were contention when trying to take the monitor lock between update intervals", new[] { net30, net31, net50 }),
                new("threadpool-queue-length", "ThreadPool Work Items Queue Length", new[] { net30, net31, net50 }),
                new("threadpool-completed-items-count", "ThreadPool Completed Work Items Count", new[] { net30, net31, net50 }),
                new("active-timer-count", "Number of timers that are currently active", new[] { net30, net31, net50 }),
                new("il-bytes-jitted", "Total IL bytes jitted", new[] { net50 }),
                new("methods-jitted-count", "Number of methods jitted", new[] { net50 }),
                new("gc-committed", "Size of committed memory by the GC (MB)", new[] { net60 })
            }
        ),
        new(
            "Microsoft.AspNetCore.Hosting", // Name
            "A set of performance counters provided by ASP.NET Core.", // Description
            0x0, // Keywords
            EventLevel.Informational, // Level 
            new Counter[] { // Counters
                new("requests-per-second", "Number of requests between update intervals", new[] { net30, net31, net50 }),
                new("total-requests", "Total number of requests", new[] { net30, net31, net50 }),
                new("current-requests", "Current number of requests", new[] { net30, net31, net50 }),
                new("failed-requests", "Failed number of requests", new[] { net30, net31, net50 }),
            }
        ),
        new(
            "Microsoft-AspNetCore-Server-Kestrel", // Name
            "A set of performance counters provided by Kestrel.", // Description
            0, // Keywords
            EventLevel.Informational, // Level
            new Counter[] {
                new("connections-per-second", "Number of connections between update intervals", new[] { net50 }),
                new("total-connections", "Total Connections", new[] { net50 }),
                new("tls-handshakes-per-second", "Number of TLS Handshakes made between update intervals", new[] { net50 }),
                new("total-tls-handshakes", "Total number of TLS handshakes made", new[] { net50 }),
                new("current-tls-handshakes", "Number of currently active TLS handshakes", new[] { net50 }),
                new("failed-tls-handshakes", "Total number of failed TLS handshakes", new[] { net50 }),
                new("current-connections", "Number of current connections", new[] { net50 }),
                new("connection-queue-length", "Length of Kestrel Connection Queue", new[] { net50 }),
                new("request-queue-length", "Length total HTTP request queue", new[] { net50 }),
            }
        ),
        new(
            "System.Net.Http",
            "A set of performance counters for System.Net.Http",
            0, // Keywords
            EventLevel.Informational, // Level
            new Counter[] {
                new("requests-started", "Total Requests Started", new[] { net50 }),
                new("requests-started-rate", "Number of Requests Started between update intervals", new[] { net50 }),
                new("requests-aborted", "Total Requests Aborted", new[] { net50 }),
                new("requests-aborted-rate", "Number of Requests Aborted between update intervals", new[] { net50 }),
                new("current-requests", "Current Requests", new[] { net50 })
            }
        ),
        new(
            "System.Net.NameResolution",
            "A set of performance counters for DNS lookups",
            0,
            EventLevel.Informational,
            new Counter[] {
                new("dns-lookups-requested", "The number of DNS lookups requested since the process started", new[] { net50 }),
                new("dns-lookups-duration", "Average DNS Lookup Duration", new[] { net50 }),
            }
        ),
        new(
            "System.Net.Security",
            "A set of performance counters for TLS",
            0,
            EventLevel.Informational,
            new Counter[] {
                new("tls-handshake-rate", "The number of TLS handshakes completed per update interval", new[] { net50 }),
                new("total-tls-handshakes", "The total number of TLS handshakes completed since the process started", new[] { net50 }),
                new("current-tls-handshakes", "The current number of TLS handshakes that have started but not yet completed", new[] { net50 }),
                new("failed-tls-handshakes", "The total number of TLS handshakes failed since the process started", new[] { net50 }),
                new("all-tls-sessions-open", "The number of active all TLS sessions", new[] { net50 }),
                new("tls10-sessions-open", "The number of active TLS 1.0 sessions", new[] { net50 }),
                new("tls11-sessions-open", "The number of active TLS 1.1 sessions", new[] { net50 }),
                new("tls12-sessions-open", "The number of active TLS 1.2 sessions", new[] { net50 }),
                new("tls13-sessions-open", "The number of active TLS 1.3 sessions", new[] { net50 }),
                new("all-tls-handshake-duration", "The average duration of all TLS handshakes", new[] { net50 }),
                new("tls10-handshake-duration", "The average duration of TLS 1.0 handshakes", new[] { net50 }),
                new("tls11-handshake-duration", "The average duration of TLS 1.1 handshakes", new[] { net50 }),
                new("tls12-handshake-duration", "The average duration of TLS 1.2 handshakes", new[] { net50 }),
                new("tls13-handshake-duration", "The average duration of TLS 1.3 handshakes", new[] { net50 })
            }
        ),
        new(
            "System.Net.Sockets",
            "A set of performance counters for System.Net.Sockets",
            0,
            EventLevel.Informational,
            new Counter[] {
                new("outgoing-connections-established", "The total number of outgoing connections established since the process started", new[] { net50 }),
                new("incoming-connections-established", "The total number of incoming connections established since the process started", new[] { net50 }),
                new("bytes-received", "The total number of bytes received since the process started", new[] { net50 }),
                new("bytes-sent", "The total number of bytes sent since the process started", new[] { net50 }),
                new("datagrams-received", "The total number of datagrams received since the process started", new[] { net50 }),
                new("datagrams-sent", "The total number of datagrams sent since the process started", new[] { net50 }),
            }
        )
    };
}