using Simple.Dotnet.Diagnostics.Core.Handlers;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Interceptors;
using Simple.Dotnet.Diagnostics.Interceptors.AspNetCore;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public sealed class HttpDumps
{
    public static async Task<IResult> Write(WriteDumpCommand command, ILogger logger, CancellationToken token)
    {
        var result = await Dumps.Handle(command, token);
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(result, default));

        logger.LogWarning(
            result.Error.Exception, 
            "Write dump for process: {ProcessId}/{ProcessName} failed with error. Message: {ErrorMessage}.", 
            command.ProcessId, command.Name, result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpWriteDumpFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpDumpValidationError, e.Validation!),
        });
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static IResult Read(ReadDumpQuery query, ILogger logger, CancellationToken token)
    {
        var result = Dumps.Handle(query, token);
        if (result.IsOk) return Results.File(result.Ok!, fileDownloadName: query.Output);

        logger.LogWarning(
            result.Error.Exception,
            "Read dump for output {Output} failed with error. Message: {ErrorMessage}",
            query.Output, result.Error.Validation ?? result.Error.Exception!.Message);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpReadDumpFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpDumpValidationError, e.Validation!),
        });

        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static IResult Delete(DeleteDumpCommand command, ILogger logger, CancellationToken token)
    {
        var result = Dumps.Handle(command, token);
        if (result.IsOk) return Results.Ok();

        logger.LogWarning(
            result.Error.Exception,
            "Delete dump for output {Output} failed with error. Message: {ErrorMessage}",
            command.Output, result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpReadDumpFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpDumpValidationError, e.Validation!),
        });
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static IResult GetDumps(ILogger logger, CancellationToken token)
    {
        var result = Dumps.Handle(new GetAllDumpsQuery(), token);
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(result, default));

        logger.LogWarning(
            result.Error.Exception,
            "Get available dumps failed with error. Message: {ErrorMessage}", result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpGetDumpsFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpDumpValidationError, e.Validation!),
        });

        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }
}

