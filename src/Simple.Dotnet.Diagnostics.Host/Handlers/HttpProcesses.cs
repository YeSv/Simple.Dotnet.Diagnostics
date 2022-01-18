using Simple.Dotnet.Diagnostics.Core.Handlers;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public sealed class HttpProcesses
{
    public static IResult Get(ILogger logger, CancellationToken token)
    {
        var result = Processes.Handle(token);
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(result, default));

        logger.LogError(
            result.Error.Exception,
            "Get processes failed with error: {ErrorMessage}", result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpGetProcessesFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpGetProcessesValidationError, e.Validation!),
        });
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static IResult Get(in GetProcessByIdQuery query, ILogger logger, CancellationToken token)
    {
        var result = Processes.Handle(query, token);
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(result, default));

        logger.LogError(
            result.Error.Exception,
            "Get processes by id {ProcessId} failed with error: {ErrorMessage}", query.ProcessId, result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpGetProcessByIdFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpGetProcessesValidationError, e.Validation!),
        });
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static IResult Get(in GetProcessesByNameQuery query, ILogger logger, CancellationToken token)
    {
        var result = Processes.Handle(query, token);
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(result, default));

        logger.LogError(
            result.Error.Exception,
            "Get processes by name {ProcessName} failed with error: {ErrorMessage}", query.ProcessName, result.Error);

        var response = ResponseMapper.ToResponse(result, e => e switch
        {
            { Exception: not null } => new(ErrorCodes.HttpGetProcessByNameFailed, e.Exception!.Message),
            { Validation: not null } => new(ErrorCodes.HttpGetProcessesValidationError, e.Validation!),
        });
        return JsonResult.Create(response, statusCode: ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }
}
