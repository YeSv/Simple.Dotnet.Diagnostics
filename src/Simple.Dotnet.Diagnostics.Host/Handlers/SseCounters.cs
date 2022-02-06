using Simple.Dotnet.Diagnostics.Actions;
using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Streams.Streams;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public sealed class SseCounters
{
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
                { Exception: not null } => new(ErrorCodes.SseCountersFailed, e.Exception!.Message),
                { Validation: not null } => new(ErrorCodes.SseCountersValidationError, e.Validation!)
            });
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        var stream = new SseStream(context.Response, context.RequestServices.GetJsonOptions());

        var actionName = $"sse-action-{Guid.NewGuid()}";
        var action = ActionTypes.OneShot(actionName, logger, () => stream, () => countersResult.Ok!);

        token.Register(() => registry.Cancel(actionName));

        var registerResult = await registry.Register(actionName, action);
        if (!registerResult.IsOk)
        {
            logger.LogWarning(registerResult.Error, "Failed to register a new sse action");

            var response = ResponseMapper.ToResponse(UniResult.Error<Unit, Exception>(registerResult.Error!), e => new(ErrorCodes.SseCountersFailed, "Failed to register an action"));
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        var actionResult = await registerResult.Ok!;
        if (!actionResult.IsOk)
        {
            logger.LogWarning(actionResult.Error, "An error occurred while executing an action. Action: {Action}", actionName);

            var response = ResponseMapper.ToResponse(UniResult.Error<Unit, Exception>(actionResult.Error!), e => new(ErrorCodes.SseCountersFailed, "An error occurred while executing an action"));
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        return Results.Ok();
    }
}
