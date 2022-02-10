namespace Simple.Dotnet.Diagnostics.Host;

public sealed class AppConfig
{
    public StartupCountersConfig[] StartupCounters { get; set; } = Array.Empty<StartupCountersConfig>();
}

public sealed class StartupCountersConfig
{
    public string ActionName { get; set; } = string.Empty;
    public string StreamType { get; set; } = string.Empty;

    public CountersQueryConfig? CountersQuery { get; set; }
}

public sealed class CountersQueryConfig
{
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public string? Providers { get; set; }
    public uint? RefreshInterval { get; set; }
}