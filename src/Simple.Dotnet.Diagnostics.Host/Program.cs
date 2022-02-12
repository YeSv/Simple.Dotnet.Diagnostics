using Microsoft.AspNetCore.Mvc;
using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Core.Handlers;
using Simple.Dotnet.Diagnostics.Host;
using Simple.Dotnet.Diagnostics.Host.AspNetCore.Health;
using Simple.Dotnet.Diagnostics.Host.AspNetCore.HostedServices;
using Simple.Dotnet.Diagnostics.Host.Handlers;
using Simple.Dotnet.Diagnostics.Host.Interceptors;
using Simple.Dotnet.Diagnostics.Streams.Streams;
using System.Text.Json;
using System.Text.Json.Serialization;

// Pre-create required directories
Directory.CreateDirectory(Paths.Handle(new GetLocalPathForDirectoryNameQuery(Dumps.DumpsDir), default).Ok!);
Directory.CreateDirectory(Paths.Handle(new GetLocalPathForDirectoryNameQuery(Traces.TracesDir), default).Ok!);
Directory.CreateDirectory(Paths.Handle(new GetLocalPathForDirectoryNameQuery(Interceptors.InterceptorsDir), default).Ok!);

// Start application
var builder = WebApplication.CreateBuilder(args);
var interceptors = Interceptors.Load(new(Paths.Handle(new GetLocalPathForDirectoryNameQuery(Interceptors.InterceptorsDir), default).Ok!)).ToList();

// Utils
builder.Logging.ClearProviders().AddConsole();

// Services
builder.Services
    .Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    {
        o.SerializerOptions.WriteIndented = true;
        o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    })
    .Configure<AppConfig>(builder.Configuration.GetSection(nameof(AppConfig)))
    
    // Services
    .AddSingleton<IConfiguration>(builder.Configuration)
    .AddSingleton<ActionsRegistry>()

    // Hosted services

    .AddHostedService<LoggerCountersHostedService>()

    // Health
    .AddSingleton<ActionsHealthCheck>()
    .AddHealthChecks()
    .AddCheck<ActionsHealthCheck>("actions", timeout: TimeSpan.FromSeconds(5));

interceptors.ForEach(i => i.Intercept(builder));

// Build app
var app = builder.Build();

// Processes
app.MapGet("/processes", (
    [FromServices] ILogger<HttpProcesses> logger,
    CancellationToken token) => HttpProcesses.Get(logger, token));

app.MapGet("/processes/pname/{pname}", (
    [FromRoute(Name = "pname")] string name,
    [FromServices] ILogger<HttpProcesses> logger,
    CancellationToken token) => HttpProcesses.Get(new GetProcessesByNameQuery(name), logger, token));

app.MapGet("/processes/pid/{pid}", (
    [FromRoute(Name = "pid")] int processId,
    [FromServices] ILogger<HttpProcesses> logger,
    CancellationToken token) => HttpProcesses.Get(new GetProcessByIdQuery(processId), logger, token));

// Dumps
app.MapPost("/dumps/write", (
    [FromQuery(Name = "pid")] int? processId, 
    [FromQuery(Name = "pname")] string? processName, 
    [FromQuery(Name = "type")] DumpType? type, 
    [FromServices] ILogger<HttpDumps> logger,
    CancellationToken token) => HttpDumps.Write(new(processId, processName, type, default), logger, token));

app.MapGet("/dumps", (
    [FromQuery] string name, 
    [FromServices] ILogger<HttpDumps> logger, 
    CancellationToken token) => HttpDumps.Read(new(name), logger, token));

app.MapGet("/dumps/all", (
    [FromServices] ILogger<HttpDumps> logger, 
    CancellationToken token) => HttpDumps.GetDumps(logger, token));

app.MapDelete("/dumps", (
    [FromQuery] string name, 
    [FromServices] ILogger<HttpDumps> logger, 
    CancellationToken token) => HttpDumps.Delete(new(name), logger, token));

// Traces

app.MapPost("/traces/write", (
    [FromQuery(Name = "pid")] int? processId,
    [FromQuery(Name = "pname")] string? processName,
    [FromQuery(Name = "duration")] TimeSpan? duration,
    [FromServices] ILogger<HttpTraces> logger,
    CancellationToken token) => HttpTraces.Write(new(processId, processName, duration, default), logger, token));

app.MapGet("/traces", (
    [FromQuery] string name,
    [FromServices] ILogger<HttpDumps> logger,
    CancellationToken token) => HttpTraces.Read(new(name), logger, token));

app.MapGet("/traces/all", (
    [FromServices] ILogger<HttpTraces> logger,
    CancellationToken token) => HttpTraces.GetTraces(logger, token));

app.MapDelete("/traces", (
    [FromQuery] string name,
    [FromServices] ILogger<HttpTraces> logger,
    CancellationToken token) => HttpTraces.Delete(new(name), logger, token));

// Counters
app.MapGet("/counters/ws", (
    [FromQuery(Name = "pid")] int? processId,
    [FromQuery(Name = "pname")] string? processName,
    [FromQuery(Name = "providers")] string? providers,
    [FromQuery(Name = "interval")] uint? refreshInterval,
    [FromServices] ILogger<WsCounters> logger,
    [FromServices] ActionsRegistry registry,
    HttpContext context,
    CancellationToken token) => WsCounters.Handle(new(processId, providers, processName, refreshInterval), context, logger, registry, token));

app.MapGet("/counters/sse", (
    [FromQuery(Name = "pid")] int? processId,
    [FromQuery(Name = "pname")] string? processName,
    [FromQuery(Name = "providers")] string? providers,
    [FromQuery(Name = "interval")] uint? refreshInterval,
    [FromServices] ILogger<SseCounters> logger,
    [FromServices] ActionsRegistry registry,
    HttpContext context,
    CancellationToken token) => SseCounters.Handle(new(processId, providers, processName, refreshInterval), context, registry, logger, token));

app.MapPost("/counters/kafka", (
    [FromQuery(Name = "pid")] int? processId,
    [FromQuery(Name = "pname")] string? processName,
    [FromQuery(Name = "providers")] string? providers,
    [FromQuery(Name = "interval")] uint? refreshInterval,
    [FromBody] KafkaConfig config,
    [FromServices] ILogger<KafkaCounters> logger,
    [FromServices] ActionsRegistry registry) => KafkaCounters.Handle(new(processId, providers, processName, refreshInterval), config, registry, logger));

app.MapPost("/counters/mongo", (
    [FromQuery(Name = "pid")] int? processId,
    [FromQuery(Name = "pname")] string? processName,
    [FromQuery(Name = "providers")] string? providers,
    [FromQuery(Name = "interval")] uint? refreshInterval,
    [FromBody] MongoConfig config,
    [FromServices] ILogger<MongoCounters> logger,
    [FromServices] ActionsRegistry registry) => MongoCounters.Handle(new(processId, providers, processName, refreshInterval), config, registry, logger));

// Actions
app.MapGet("/actions", (
    [FromServices] ILogger<HttpActions> logger,
    [FromServices] ActionsRegistry registry,
    CancellationToken token) => HttpActions.GetAll(new(), logger, registry, token));

app.MapDelete("/actions", (
    [FromQuery(Name = "name")] string name,
    [FromServices] ILogger<HttpActions> logger,
    [FromServices] ActionsRegistry registry,
    CancellationToken token) => HttpActions.Cancel(new(name), logger, registry, token));

// Health
app.MapHealthChecks("/healthcheck", new()
{
    AllowCachingResponses = false,
    ResponseWriter = HealthFormatter.WriteResponse
});

interceptors.ForEach(interceptor => interceptor.Intercept(app));

// Run
app.Run();