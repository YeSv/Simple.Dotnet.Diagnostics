using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Core.Handlers;

public readonly record struct GetLocalPathForFileNameQuery(string FileName, string? Directory);

public readonly record struct GenerateFullPathCommand(string FileName, string Extension, string? Directory);

public readonly record struct GetLocalPathForDirectoryNameQuery(string Directory);

public static class Paths
{
    static readonly string CurrentDir = Directory.GetCurrentDirectory();

    public static Result<string, DiagnosticsError> Handle(in GenerateFullPathCommand command, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrEmpty(command.FileName)) return Result.Error<string, DiagnosticsError>(new($"{nameof(GenerateFullPathCommand.FileName)} can't be empty"));

            var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? $"{command.FileName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{command.Extension}"
                : $"{command.FileName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

            return Handle(new GetLocalPathForFileNameQuery(fileName, command.Directory), token);
        }
        catch (Exception ex)
        {
            return Result.Error<string, DiagnosticsError>(new(ex));
        }
    }

    public static Result<string, DiagnosticsError> Handle(in GetLocalPathForDirectoryNameQuery query, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrEmpty(query.Directory)) return Result.Error<string, DiagnosticsError>(new($"{nameof(GetLocalPathForDirectoryNameQuery.Directory)} can't be empty"));
            return Result.Ok<string, DiagnosticsError>(Path.GetFullPath(Path.Combine(CurrentDir, query.Directory)));
        }
        catch (Exception ex)
        {
            return Result.Error<string, DiagnosticsError>(new(ex));
        }
    }

    public static Result<string, DiagnosticsError> Handle(in GetLocalPathForFileNameQuery query, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query.FileName)) return Result.Error<string, DiagnosticsError>(new($"{nameof(GetLocalPathForFileNameQuery.FileName)} can't be empty"));

            var fileRelativePath = string.IsNullOrWhiteSpace(query.Directory) ? query.FileName : Path.Combine(query.Directory, query.FileName);
            return Result.Ok<string, DiagnosticsError>(Path.GetFullPath(Path.Combine(CurrentDir, fileRelativePath)));
        }
        catch (Exception ex)
        {
            return Result.Error<string, DiagnosticsError>(new(ex));
        }
    }
}
