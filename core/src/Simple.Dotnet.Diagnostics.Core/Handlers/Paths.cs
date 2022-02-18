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
    static readonly string StorageDir = Path.Combine(Directory.GetCurrentDirectory(), "storage");

    public static Result<string, DiagnosticsError> Handle(in GenerateFullPathCommand command, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrEmpty(command.FileName)) 
                return new(new DiagnosticsError($"{nameof(GenerateFullPathCommand.FileName)} can't be empty"));

            var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? $"{command.FileName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{command.Extension}"
                : $"{command.FileName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

            return Handle(new GetLocalPathForFileNameQuery(fileName, command.Directory), token);
        }
        catch (Exception ex)
        {
            return new(new DiagnosticsError(ex));
        }
    }

    public static Result<string, DiagnosticsError> Handle(in GetLocalPathForDirectoryNameQuery query, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrEmpty(query.Directory)) 
                return new(new DiagnosticsError($"{nameof(GetLocalPathForDirectoryNameQuery.Directory)} can't be empty"));

            return new(Path.GetFullPath(Path.Combine(StorageDir, query.Directory)));
        }
        catch (Exception ex)
        {
            return new(new DiagnosticsError(ex));
        }
    }

    public static Result<string, DiagnosticsError> Handle(in GetLocalPathForFileNameQuery query, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query.FileName)) 
                return new(new DiagnosticsError($"{nameof(GetLocalPathForFileNameQuery.FileName)} can't be empty"));

            var fileRelativePath = string.IsNullOrWhiteSpace(query.Directory) ? query.FileName : Path.Combine(query.Directory, query.FileName);
            return new(Path.GetFullPath(Path.Combine(StorageDir, fileRelativePath)));
        }
        catch (Exception ex)
        {
            return new(new DiagnosticsError(ex));
        }
    }
}
