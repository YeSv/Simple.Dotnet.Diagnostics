using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Simple.Dotnet.Diagnostics.Actions;
using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Diagnostics.Interceptors;
using Simple.Dotnet.Diagnostics.Interceptors.AspNetCore;
using Simple.Dotnet.Utilities.Results;
using System.Net;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.ServerSentEvents;

internal static class ErrorCodes
{
    public const int CountersFailed = 10001;
    public const int ValidationError = 10002;

    public static int ToHttpCode(int errorCode) => errorCode switch
    {
        ValidationError => (int)HttpStatusCode.BadRequest,
        CountersFailed => (int)HttpStatusCode.InternalServerError,
    };
}

internal sealed class ServerSentEventsCounters
{
    static readonly JsonSerializerOptions DefaultJsonOptions = new() { WriteIndented = false };

    public static async Task<IResult> Handle(
        CountersQuery query,
        HttpContext context,
        ActionsRegistry registry,
        ILogger logger,
        CancellationToken token)
    {
        var countersResult = Counters.Handle(ref query, token);
        if (!countersResult.IsOk)
        {
            logger.LogError(countersResult.Error.Exception,
                "Failed to create counters event pipe for a process: {ProcessId}/{ProcessName}. Message: {ErrorMessage}",
                query.ProcessId, query.ProcessName, countersResult.Error);

            var response = ResponseMapper.ToResponse(countersResult, e => e switch
            {
                { Exception: not null } => new(ErrorCodes.CountersFailed, e.Exception!.Message),
                { Validation: not null } => new(ErrorCodes.ValidationError, e.Validation!)
            });
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        var stream = new SseStream(context.Response, context.RequestServices.GetJsonOptions() switch
        {
            null => DefaultJsonOptions,
            var o => new(o) { WriteIndented = false }
        });

        var actionName = $"sse-action-{Guid.NewGuid()}";
        var action = ActionTypes.OneShot(actionName, logger, () => stream, () => countersResult.Ok!);

        token.Register(() => registry.Cancel(actionName), false);

        var registerResult = await registry.Register(actionName, action);
        if (!registerResult.IsOk)
        {
            logger.LogWarning(registerResult.Error, "Failed to register a new sse action");

            var response = ResponseMapper.ToResponse(UniResult.Error<Unit, Exception>(registerResult.Error!), e => new(ErrorCodes.CountersFailed, "Failed to register an action"));
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        var actionResult = await registerResult.Ok!;
        if (token.IsCancellationRequested)
        {
            logger.LogInformation("Request for an sse action was cancelled. Action: {Action}", actionName);
            return Results.Ok();
        }

        if (!actionResult.IsOk)
        {
            logger.LogWarning(actionResult.Error, "An error occurred while executing an action. Action: {Action}", actionName);

            var response = ResponseMapper.ToResponse(UniResult.Error<Unit, Exception>(actionResult.Error!), e => new(ErrorCodes.CountersFailed, "An error occurred while executing an action"));
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        return Results.Ok();
    }
}