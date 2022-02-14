using Simple.Dotnet.Diagnostics.Core.Handlers;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Interceptors;
using Simple.Dotnet.Diagnostics.Interceptors.AspNetCore;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public sealed class HttpTraces
{
    public static async Task<IResult> Write(WriteTraceCommand command, ILogger logger, CancellationToken token)
    {
        var result = await Traces.Handle(command, token);
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(result, default));

        logger.LogWarning(
            result.Error.Exception,
            "Write trace for a process: {ProcessId}/{ProcessName} failed with error. Message: {ErrorMessage}.",
            command.ProcessId, command.Name, result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpWriteTraceFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpTracesValidationError, e.Validation!)
        });

        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static IResult Read(ReadTraceQuery query, ILogger logger, CancellationToken token)
    {
        var result = Traces.Handle(query, token);
        if (result.IsOk) return Results.File(result.Ok!, fileDownloadName: query.Output);

        logger.LogWarning(
            result.Error.Exception,
            "Read trace for output {Output} failed with error. Message: {ErrorMessage}",
            query.Output, result.Error.Validation ?? result.Error.Exception!.Message);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpReadTraceFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpTracesValidationError, e.Validation!),
        });

        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static IResult Delete(DeleteTraceCommand command, ILogger logger, CancellationToken token)
    {
        var result = Traces.Handle(command, token);
        if (result.IsOk) return Results.Ok();

        logger.LogWarning(
            result.Error.Exception,
            "Delete trace for output {Output} failed with error. Message: {ErrorMessage}",
            command.Output, result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpDeleteTraceFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpTracesValidationError, e.Validation!),
        });
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static IResult GetTraces(ILogger logger, CancellationToken token)
    {
        var result = Traces.Handle(new GetAllTracesQuery(), token);
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(result, default));

        logger.LogWarning(
            result.Error.Exception,
            "Get available traces failed with error. Message: {ErrorMessage}", result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpGetTracesFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpTracesValidationError, e.Validation!),
        });

        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }
}
