using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Streams;
using Simple.Dotnet.Diagnostics.Streams.Actors;
using Simple.Dotnet.Diagnostics.Streams.Streams;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public sealed class SseCounters
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
                { Exception: not null } => new(ErrorCodes.SseCountersFailed, e.Exception!.Message),
                { Validation: not null } => new(ErrorCodes.SseCountersValidationError, e.Validation!)
            });
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }

        // Create a cancellation for event pipe in case if stream fails
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);

        try
        {
            // Create stream
            var stream = new SseStream(context.Response, context.RequestServices.GetJsonOptions());

            // Create actor
            var actorId = await actor.AddStream(stream);

            // Wait for either closure or error in http stream or counters error
            var (streamTask, countersTask) = (stream.Completion, countersResult.Ok!.Start(m => actor.Send(SendEventCommand.Create(m), actorId), token));
            var finishedTask = await Task.WhenAny(streamTask, countersTask);

            cancellation.Cancel(); // Stopping counters subscription task (if it has not already finished)
            await actor.RemoveStream(actorId, false); // Wait for a stream removal

            if (streamTask.Result is { Error: not null })
                logger.LogError(streamTask.Result.Error, "{HandlerType} for {ActorId} finished with HttpStream error", nameof(SseCounters), actorId);

            if (countersTask.Result is { IsOk: false, Error: var countersException })
                logger.LogError(countersException, "{HandlerType} for {ActorId} finished with Counters error", nameof(SseCounters), actorId);

            var error = streamTask.Result.Error ?? countersTask.Result.Error;
            if (error == null) return Results.Ok();

            var response = ResponseMapper.ToResponse(UniResult.Error<Unit, Exception>(error), e => new(ErrorCodes.SseCountersFailed, e!.Message));
            return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
        }
        finally
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

}
