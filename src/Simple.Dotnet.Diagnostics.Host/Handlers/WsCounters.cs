using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Streams;
using Simple.Dotnet.Diagnostics.Streams.Actors;
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
        RootActor actor,
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

        // Create a cancellation for event pipe in case if stream fails
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

        try
        {
            // Accept WS connection and subscribe to it
            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            var stream = new WebSocketStream(ws, context.RequestServices.GetJsonOptions());

            // Create actor
            var actorId = await actor.AddStream(stream);

            // Wait for either closure or error in http stream or counters error
            var (streamTask, countersTask) = (stream.Completion, countersResult.Ok!.Start(m => actor.Send(SendEventCommand.Create(m), actorId), token));
            var finishedTask = await Task.WhenAny(streamTask, countersTask);

            cancellation.Cancel(); // Stopping counters subscription task (if it has not already finished)
            await actor.RemoveStream(actorId, false); // Wait for a stream removal

            // Close web socket with correct reason
            await ((finishedTask == streamTask) switch
            {
                true => CloseOnWsError(ws, logger, actorId, streamTask.Result, token),
                false => CloseOnCountersError(ws, logger, actorId, countersTask.Result, token)
            });

            if (streamTask.Result is { Exception: not null })
                logger.LogError(streamTask.Result.Exception, "{HandlerType} for {ActorId} finished with HttpStream error", nameof(WsCounters), actorId);

            if (countersTask.Result is { IsOk: false, Error: var countersException })
                logger.LogError(countersException, "{HandlerType} for {ActorId} finished with Counters error", nameof(WsCounters), actorId);


            return Results.Ok();
        }
        finally
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    static Task CloseOnWsError(WebSocket ws, ILogger logger, Guid actorId, WebSocketResult wsResult, CancellationToken token) =>
        ws.CloseAsync(wsResult.Type switch
        {
            WebSocketResultType.WsException => WebSocketCloseStatus.ProtocolError,
            WebSocketResultType.ClosedByClient => WebSocketCloseStatus.NormalClosure,
            WebSocketResultType.UnhandledException => WebSocketCloseStatus.InternalServerError,
            _ => WebSocketCloseStatus.InternalServerError
        }, $"Closing WebSocket due to '{wsResult.Type}'. SubscriptionId: {actorId}", token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) return;
            if (t.IsCanceled) return;

            if (t.Exception?.InnerException is not OperationCanceledException)
                logger.LogError(t.Exception, "Failed to close connection to a web socket in {CountersHandler}. ActorId: {ActorId}", nameof(WsCounters), actorId);
        });

    static Task CloseOnCountersError(WebSocket ws, ILogger logger, Guid actorId, UniResult<Unit, Exception> subscriptionResult, CancellationToken token) =>
        ws.CloseAsync(subscriptionResult switch
        {
            { IsOk: false } => WebSocketCloseStatus.InternalServerError,
            _ => WebSocketCloseStatus.NormalClosure
        }, $"Closing WebSocket due to internal countes closure. Error: {(subscriptionResult.IsOk ? "<Normal closure>" : "<Unhandled error>")}. SubscriptionId: {actorId}", token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) return;
            if (t.IsCanceled) return;

            if (t.Exception?.InnerException is not OperationCanceledException)
                logger.LogError(t.Exception, "Failed to close connection to a web socket in {CountersHandler}. ActorId: {ActorId}", nameof(WsCounters), actorId);
        });
}
