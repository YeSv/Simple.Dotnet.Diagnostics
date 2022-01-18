namespace Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;

public enum EventMetricType : byte { Counter, Gauge }

public readonly record struct EventMetric(string Name, double Value, EventMetricType Type);
