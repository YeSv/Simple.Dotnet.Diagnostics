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

        var actionResult = await registry.Schedule(actionName, action);
        if (!actionResult.IsOk)
        {
            var response = ResponseMapper.ToResponse(UniResult.Error<Unit, Exception>(actionResult.Error!), e => new(ErrorCodes.SseCountersFailed, e!.Message));
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        return Results.Ok();
    }
}
