using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Simple.Dotnet.Diagnostics.Core.Handlers;

public readonly record struct GetAllTracesQuery();

public readonly record struct ReadTraceQuery(string Output);

public readonly record struct DeleteTraceCommand(string Output);

public readonly record struct WriteTraceCommand(int? ProcessId, string? Name, TimeSpan? Duration, string? Output);

public static class Traces
{
    static readonly EventPipeProvider[] TraceProviders = new[]
    {
        new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Default),
        new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.None)
    };

    public static readonly string TracesDir = "traces";

    public static Result<FileStream, DiagnosticsError> Handle(in ReadTraceQuery query, CancellationToken token)
    {
        try
        {
            var fullPathResult = Paths.Handle(new GetLocalPathForFileNameQuery(query.Output, TracesDir), token);
            if (!fullPathResult.IsOk) return new(fullPathResult.Error);

            if (!File.Exists(fullPathResult.Ok)) return new(new DiagnosticsError("File does not exist"));

            return new(new FileStream(fullPathResult.Ok, FileMode.Open, FileAccess.Read));
        }
        catch (Exception ex)
        {
            return new(new DiagnosticsError(ex));
        }
    }

    public static Result<Unit, DiagnosticsError> Handle(in DeleteTraceCommand command, CancellationToken token)
    {
        try
        {
            var fullPathResult = Paths.Handle(new GetLocalPathForFileNameQuery(command.Output, TracesDir), token);
            if (!fullPathResult.IsOk) return new(fullPathResult.Error);

            if (!File.Exists(fullPathResult.Ok)) return new(new DiagnosticsError("File does not exist"));

            File.Delete(fullPathResult.Ok);

            return new(Unit.Shared);
        }
        catch (Exception ex)
        {
            return new(new DiagnosticsError(ex));
        }
    }

    public static ValueTask<Result<string, DiagnosticsError>> Handle(WriteTraceCommand command, CancellationToken token)
    {
        if (command.ProcessId is < 0)
            return new(Result.Error<string, DiagnosticsError>(new DiagnosticsError("Query contains not valid process id")));

        if (!command.ProcessId.HasValue && string.IsNullOrWhiteSpace(command.Name))
            return new(Result.Error<string, DiagnosticsError>(new DiagnosticsError("Process name or process id should be specified")));

        if (!string.IsNullOrWhiteSpace(command.Name))
        {
            var result = Processes.Handle(new GetProcessByNameQuery(command.Name), token);
            if (!result.IsOk) return new(Result.Error<string, DiagnosticsError>(result.Error));

            command = command with { ProcessId = result.Ok.ProcessId };
        }

        var idLookup = Processes.Handle(new GetProcessByIdQuery(command.ProcessId!.Value), token);
        if (!idLookup.IsOk) return new(Result.Error<string, DiagnosticsError>(idLookup.Error));

        if (string.IsNullOrWhiteSpace(command.Output)) command = command with { Output = command.Name ?? command.ProcessId?.ToString() ?? Guid.NewGuid().ToString() };

        var fullPathResult = Paths.Handle(new GenerateFullPathCommand(command.Output, "nettrace", TracesDir), token);
        if (!fullPathResult.IsOk) return new(fullPathResult);

        return new(Task.Run(() =>
        {
            try
            {
                using var session = new DiagnosticsClient(command.ProcessId!.Value).StartEventPipeSession(TraceProviders);
                using var timer = new CancellationTokenSource(command.Duration ?? TimeSpan.FromSeconds(30));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, token);

                linked.Token.Register(s => ((EventPipeSession)s!).Stop(), session, false);

                using var fileStream = new FileStream(fullPathResult.Ok!, FileMode.Create, FileAccess.Write);

                session.EventStream.CopyTo(fileStream);

                return UniResult.Ok<Unit, Exception>(Unit.Shared);
            }
            catch (Exception ex)
            {
                return new(ex);
            }
        }, token).ContinueWith<Result<string, DiagnosticsError>>(t => t.Result switch
        {
            { IsOk: true } => new(Path.GetFileName(fullPathResult.Ok!)),
            var e => new(new DiagnosticsError(e.Error!))
        }));
    }

    public static Result<string[], DiagnosticsError> Handle(GetAllTracesQuery query, CancellationToken token)
    {
        try
        {
            var tracesPathResult = Paths.Handle(new GetLocalPathForDirectoryNameQuery(TracesDir), token);
            if (!tracesPathResult.IsOk) return Result.Error<string[], DiagnosticsError>(tracesPathResult.Error);

            if (!Directory.Exists(tracesPathResult.Ok!)) return Result.Error<string[], DiagnosticsError>(new("Traces directory does not exist"));

            var filePaths = Directory.GetFiles(tracesPathResult.Ok!);
            return Result.Ok<string[], DiagnosticsError>(filePaths.Length switch
            {
                0 => Array.Empty<string>(),
                1 => new[] { Path.GetFileName(filePaths[0]) },
                _ => filePaths.Select(p => Path.GetFileName(p)).ToArray()
            });
        }
        catch (Exception ex)
        {
            return Result.Error<string[], DiagnosticsError>(new(ex));
        }
    }
}

