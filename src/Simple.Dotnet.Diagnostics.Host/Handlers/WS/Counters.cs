using Simple.Dotnet.Diagnostics.Core.Handlers;
using Simple.Dotnet.Diagnostics.Host.HttpResults;
using Simple.Dotnet.Diagnostics.Streams;
using Simple.Dotnet.Diagnostics.Streams.Streams;
using System.Net.WebSockets;

namespace Simple.Dotnet.Diagnostics.Host.Handlers.WS;

public sealed class WsCounters
{
    // Non-retryable counters subscription based on WebSocket
    public static async Task<IResult> Handle(
        CountersQuery query, 
        HttpContext context, 
        ILogger logger,
        RootActor actor,
        CancellationToken token)
    {
        // Receive counters subscription
        var countersResult = Counters.Handle(ref query, token);
        if (!countersResult.IsOk) return JsonResult.Create(countersResult.Error, 400); // TODO: rewrite

        // Accept WS connection and subscribe to it
        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var stream = new WebSocketStream(ws, context.RequestServices.GetJsonOptions());

        // Create actor
        var actorId = await actor.AddStream(stream);

        // Add handlers for metrics
        countersResult.Ok!.OnMetric = m => actor.Send(SendEventCommand.Create(m), actorId);

        // Get tasks for subscription and counters
        using var countersSource = CancellationTokenSource.CreateLinkedTokenSource(token);

        // Wait for either closure or error in http stream or counters error
        var (streamTask, countersTask) = (stream.Completion, countersResult.Ok.Start(countersSource.Token));
        var finishedTask = await Task.WhenAny(streamTask, countersTask);

        countersSource.Cancel(); // Stopping counters subscription task (if it has not already finished)
        await actor.RemoveStream(actorId, false); // Wait for a stream removal

        var closureTask = (finishedTask == streamTask) switch
        {
            true => CloseOnWsError(ws, logger, actorId, streamTask.Result, token),
            false => CloseWsOnCountersError(ws, logger, actorId, countersTask.Result, token)
        };

        await Task.WhenAll(closureTask, streamTask, countersTask);

        if (streamTask.Result.Exception != null)
            logger.LogError(streamTask.Result.Exception, "{HandlerType} for {ActorId} finished with HttpStream error", nameof(WsCounters), actorId);

        if (countersTask.Result != null)
            logger.LogError(countersTask.Result, "{HandlerType} for {ActorId} finished with Counters error", nameof(WsCounters), actorId);

        // TODO: Map errors or not needed?
        return Results.Ok();
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

    static Task CloseWsOnCountersError(WebSocket ws, ILogger logger, Guid actorId, Exception? countersError, CancellationToken token) =>
        ws.CloseAsync(countersError switch
        {
            null => WebSocketCloseStatus.NormalClosure,
            var e => WebSocketCloseStatus.InternalServerError
        }, $"Closing WebSocket due to internal countes closure. Error: {countersError}. SubscriptionId: {actorId}", token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) return;
            if (t.IsCanceled) return;

            if (t.Exception?.InnerException is not OperationCanceledException)
                logger.LogError(t.Exception, "Failed to close connection to a web socket in {CountersHandler}. ActorId: {ActorId}", nameof(WsCounters), actorId);
        });
}
