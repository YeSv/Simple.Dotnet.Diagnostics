using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Simple.Dotnet.Diagnostics.Core.Handlers;

public readonly record struct ProcessInfo(int ProcessId, string ProcessName, string FileName);

public readonly record struct GetProcessByIdQuery(int ProcessId);

public readonly record struct GetProcessByNameQuery(string ProcessName);

public readonly record struct GetProcessesByNameQuery(string ProcessName);

public static class Processes
{
    public static Result<ProcessInfo, DiagnosticsError> Handle(in GetProcessByIdQuery query, CancellationToken token)
    {
        try
        {
            var publishedProcesses = DiagnosticsClient.GetPublishedProcesses();
            if (publishedProcesses == null) return Result.Error<ProcessInfo, DiagnosticsError>(new($"Process with specified id '{query.ProcessId}' was not found"));

            foreach (var processId in publishedProcesses)
            {
                if (processId != query.ProcessId) continue;

                var process = Process.GetProcessById(processId);
                if (process == null) break;

                return Result.Ok<ProcessInfo, DiagnosticsError>(new(query.ProcessId, process.ProcessName, process.MainModule?.FileName ?? string.Empty));
            }

            return Result.Error<ProcessInfo, DiagnosticsError>(new($"Process with specified id '{query.ProcessId}' was not found"));
        }
        catch (Exception ex)
        {
            return Result.Error<ProcessInfo, DiagnosticsError>(new(ex));
        }
    }

    public static Result<ProcessInfo, DiagnosticsError> Handle(in GetProcessByNameQuery query, CancellationToken token)
    {
        try
        {
            var processes = Handle(new GetProcessesByNameQuery(query.ProcessName), token);
            if (!processes.IsOk) return Result.Error<ProcessInfo, DiagnosticsError>(processes.Error);
            if (processes.Ok!.Length == 0) return Result.Error<ProcessInfo, DiagnosticsError>(new($"Process with specified name '{query.ProcessName}' was not found"));
            if (processes.Ok!.Length > 0) return Result.Error<ProcessInfo, DiagnosticsError>(new($"Found multiple processes with name '{query.ProcessName}'"));

            return Result.Ok<ProcessInfo, DiagnosticsError>(processes.Ok[0]);
        }
        catch (Exception ex)
        {
            return Result.Error<ProcessInfo, DiagnosticsError>(new (ex));
        }
    }

    public static Result<ProcessInfo[], DiagnosticsError> Handle(in GetProcessesByNameQuery query, CancellationToken token)
    {
        try
        {
            var publishedProcesses = DiagnosticsClient.GetPublishedProcesses();
            if (publishedProcesses == null) return Result.Ok<ProcessInfo[], DiagnosticsError>(Array.Empty<ProcessInfo>());

            var totalPublishedProcesses = publishedProcesses.TryGetNonEnumeratedCount(out var count) is true ? count : publishedProcesses.Count();
            if (totalPublishedProcesses == 0) return Result.Ok<ProcessInfo[], DiagnosticsError>(Array.Empty<ProcessInfo>());

            using var rent = new Rent<ProcessInfo>(totalPublishedProcesses);
            foreach (var processId in publishedProcesses)
            {
                token.ThrowIfCancellationRequested();

                var process = Process.GetProcessById(processId);
                if (process?.ProcessName != query.ProcessName) continue;

                rent.Append(new(processId, query.ProcessName, process.MainModule?.FileName ?? string.Empty));
            }

            return Result.Ok<ProcessInfo[], DiagnosticsError>(rent.Written switch
            {
                0 => Array.Empty<ProcessInfo>(),
                _ => rent.WrittenSpan.ToArray()
            });
        }
        catch (Exception ex)
        {
            return Result.Error<ProcessInfo[], DiagnosticsError>(new(ex));
        }
    }

    public static Result<ProcessInfo[], DiagnosticsError> Handle(CancellationToken token)
    {
        try
        {
            var publishedProcesses = DiagnosticsClient.GetPublishedProcesses();
            if (publishedProcesses == null) return Result.Ok<ProcessInfo[], DiagnosticsError>(Array.Empty<ProcessInfo>());

            var totalPublishedProcesses = publishedProcesses.TryGetNonEnumeratedCount(out var count) is true ? count : publishedProcesses.Count();
            if (totalPublishedProcesses == 0) return Result.Ok<ProcessInfo[], DiagnosticsError>(Array.Empty<ProcessInfo>());

            using var rent = new Rent<ProcessInfo>(totalPublishedProcesses);
            foreach (var processId in publishedProcesses)
            {
                token.ThrowIfCancellationRequested();

                var process = Process.GetProcessById(processId);
                if (process == null) continue;

                rent.Append(new(processId, process.ProcessName, process.MainModule?.FileName ?? string.Empty));
            }

            return Result.Ok<ProcessInfo[], DiagnosticsError>(rent.Written switch
            {
                0 => Array.Empty<ProcessInfo>(),
                _ => rent.WrittenSpan.ToArray()
            });
        }
        catch (Exception ex)
        {
            return Result.Error<ProcessInfo[], DiagnosticsError>(new(ex));
        }
    }
}

