using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Utilities.Results;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Simple.Dotnet.Diagnostics.Core.Handlers;

public enum DumpType : byte { Full, Heap, Mini }

public readonly record struct GetAllDumpsQuery();

public readonly record struct ReadDumpQuery(string Output);

public readonly record struct DeleteDumpCommand(string Output);

public readonly record struct WriteDumpCommand(int? ProcessId, string? Name, DumpType? Type, string? Output);

public static class Dumps
{
    public static readonly string DumpsDir = "dumps";

    public static Result<FileStream, DiagnosticsError> Handle(in ReadDumpQuery query, CancellationToken token)
    {
        try
        {
            var fullPathResult = Paths.Handle(new GetLocalPathForFileNameQuery(query.Output, DumpsDir), token);
            if (!fullPathResult.IsOk) return new(fullPathResult.Error);

            if (!File.Exists(fullPathResult.Ok)) return new(new DiagnosticsError("File does not exist"));

            return new(new FileStream(fullPathResult.Ok, FileMode.Open, FileAccess.Read));
        }
        catch (Exception ex)
        {
            return new(new DiagnosticsError(ex));
        }
    }

    public static Result<Unit, DiagnosticsError> Handle(in DeleteDumpCommand command, CancellationToken token)
    {
        try
        {
            var fullPathResult = Paths.Handle(new GetLocalPathForFileNameQuery(command.Output, DumpsDir), token);
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


    public static ValueTask<Result<string, DiagnosticsError>> Handle(WriteDumpCommand command, CancellationToken token)
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

        try
        {
            if (string.IsNullOrWhiteSpace(command.Output)) command = command with { Output = command.Name ?? command.ProcessId?.ToString() ?? Guid.NewGuid().ToString() };

            var fullPathResult = Paths.Handle(new GenerateFullPathCommand(command.Output, "dmp", DumpsDir), token);
            if (!fullPathResult.IsOk) return new(fullPathResult);

            return new(Task.Run(() =>
            {
                try
                {
                    new DiagnosticsClient(command.ProcessId!.Value).WriteDump(command.Type switch
                    {
                        DumpType.Full => Microsoft.Diagnostics.NETCore.Client.DumpType.Full,
                        DumpType.Heap => Microsoft.Diagnostics.NETCore.Client.DumpType.WithHeap,
                        DumpType.Mini => Microsoft.Diagnostics.NETCore.Client.DumpType.Normal,
                        _ => Microsoft.Diagnostics.NETCore.Client.DumpType.Normal
                    }, fullPathResult.Ok!);

                    return Result.Ok<string, DiagnosticsError>(Path.GetFileName(fullPathResult.Ok!).ToString());
                }
                catch (Exception ex)
                {
                    return Result.Error<string, DiagnosticsError>(new(ex));
                }
            }, token));
        }
        catch (Exception ex)
        {
            return new(Result.Error<string, DiagnosticsError>(new(ex)));
        }
    }

    public static Result<string[], DiagnosticsError> Handle(GetAllDumpsQuery query, CancellationToken token)
    {
        try
        {
            var dumpsPathResult = Paths.Handle(new GetLocalPathForDirectoryNameQuery(DumpsDir), token);
            if (!dumpsPathResult.IsOk) return Result.Error<string[], DiagnosticsError>(dumpsPathResult.Error);

            if (!Directory.Exists(dumpsPathResult.Ok!)) return Result.Error<string[], DiagnosticsError>(new("Dumps directory does not exist"));

            var filePaths = Directory.GetFiles(dumpsPathResult.Ok!);
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
