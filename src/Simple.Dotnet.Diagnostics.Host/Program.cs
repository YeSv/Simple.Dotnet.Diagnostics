using Microsoft.AspNetCore.Mvc;
using Simple.Dotnet.Diagnostics.Core.Handlers;
using Simple.Dotnet.Diagnostics.Host;
using Simple.Dotnet.Diagnostics.Host.Handlers;
using Simple.Dotnet.Diagnostics.Streams.Actors;

// Pre-create required directories
Directory.CreateDirectory(Paths.Handle(new GetLocalPathForDirectoryNameQuery(Dump.DumpsDir), default).Ok!);
Directory.CreateDirectory(Paths.Handle(new GetLocalPathForDirectoryNameQuery("traces"), default).Ok!); // TODO: add Trace handler

// Start application
var builder = WebApplication.CreateBuilder(args);

// Utils
builder.Logging.ClearProviders().AddConsole();

// Services
builder.Services
    .Configure<AppConfig>(builder.Configuration.GetSection(nameof(AppConfig)))
    .AddSingleton<RootActor>();

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
app.MapPost("/dump/write", (
    [FromQuery(Name = "pid")] int? processId, 
    [FromQuery(Name = "pname")] string? processName, 
    [FromQuery(Name = "type")] DumpType? type, 
    [FromServices] ILogger<HttpDump> logger,
    CancellationToken token) => HttpDump.Write(new(processId, processName, type, default), logger, token));

app.MapGet("/dump", ([FromQuery] string name, [FromServices] ILogger<HttpDump> logger, CancellationToken token) => HttpDump.Read(new(name), logger, token));

app.MapGet("/dump/all", ([FromServices] ILogger<HttpDump> logger, CancellationToken token) => HttpDump.GetDumps(logger, token));

app.MapDelete("/dump", ([FromQuery] string name, [FromServices] ILogger<HttpDump> logger, CancellationToken token) => HttpDump.Delete(new(name), logger, token));

// Counters
app.MapGet("/counters/ws", (
    [FromQuery(Name = "pid")] int? processId,
    [FromQuery(Name = "pname")] string? processName,
    [FromQuery(Name = "providers")] string[]? providers,
    [FromQuery(Name = "interval")] uint? refreshInterval,
    [FromServices] ILogger<WsCounters> logger,
    [FromServices] RootActor actor,
    HttpContext context,
    CancellationToken token) => WsCounters.Handle(new(processId, providers, processName, refreshInterval), context, logger, actor, token));

app.MapGet("/counters/sse", (
    [FromQuery(Name = "pid")] int? processId,
    [FromQuery(Name = "pname")] string? processName,
    [FromQuery(Name = "providers")] string[]? providers,
    [FromQuery(Name = "interval")] uint? refreshInterval,
    [FromServices] ILogger<WsCounters> logger,
    [FromServices] RootActor actor,
    HttpContext context,
    CancellationToken token) => SseCounters.Handle(new(processId, providers, processName, refreshInterval), context, logger, actor, token));

// Run
app.Run();