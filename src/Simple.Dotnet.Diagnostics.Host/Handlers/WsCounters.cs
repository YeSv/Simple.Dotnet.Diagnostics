using Simple.Dotnet.Diagnostics.Actions;
using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Streams.Streams;
using Simple.Dotnet.Utilities.Results;
using System.Net.WebSockets;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public sealed class WsCounters
{
    public static async Task<IResult> Handle(
        CountersQuery query, 
        HttpContext context, 
        ILogger logger,
        ActionsRegistry registry,
        CancellationToken token)
    {
        // Receive counters subscription
        var countersResult = Counters.Handle(ref query, token);
        if (!countersResult.IsOk)
        {
            logger.LogError(countersResult.Error.Exception,
                "Failed to create counters event pipe for a process: {ProcessId}/{ProcessName}. Message: {ErrorMessage}",
                query.ProcessId, query.ProcessName, countersResult.Error);

            var response = ResponseMapper.ToResponse(countersResult, e => e switch
            {
                { Exception: not null } => new(ErrorCodes.WebSocketCountersFailed, e.Exception!.Message),
                { Validation: not null } => new(ErrorCodes.WebSocketCountersValidationError, e.Validation!)
            });
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var stream = new WebSocketStream(ws, context.RequestServices.GetJsonOptions());

        var actionName = $"ws-action-{Guid.NewGuid()}";
        var action = ActionTypes.OneShot(actionName, logger, () => stream, () => countersResult.Ok!);

        token.Register(() => registry.Cancel(actionName));

        // TODO: write tests and rewrite the impl
        var registerResult = await registry.Register(actionName, action);
        if (!registerResult.IsOk)
        {
            logger.LogWarning(registerResult.Error, "Failed to register a new ws action");

            await CloseWs(ws, logger, actionName, registerResult, token);
            return Results.Ok();
        }

        var actionResult = await registerResult.Ok!;
        if (!actionResult.IsOk)
        {
            logger.LogWarning(actionResult.Error, "An error occurred while executing an action. Action: {Action}", actionName);

            await CloseWs(ws, logger, actionName, registerResult, token);
            return Results.Ok();
        }
        
        return Results.Ok();
    }

    static Task CloseWs<T>(WebSocket ws, ILogger logger, string actionName, UniResult<T, Exception> subscriptionResult, CancellationToken token) where T : class =>
        ws.CloseAsync(subscriptionResult switch
        {
            { IsOk: false } => WebSocketCloseStatus.InternalServerError,
            _ => WebSocketCloseStatus.NormalClosure
        }, $"Closing WebSocket due to internal countes closure. Error: {(subscriptionResult.IsOk ? "<Normal closure>" : "<Unhandled error>")}. Action: {actionName}", token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) return;
            if (t.IsCanceled) return;

            if (t.Exception?.InnerException is not OperationCanceledException)
                logger.LogError(t.Exception, "Failed to close connection to a web socket in {CountersHandler}. Action: {Action}", nameof(WsCounters), actionName);
        });
}
