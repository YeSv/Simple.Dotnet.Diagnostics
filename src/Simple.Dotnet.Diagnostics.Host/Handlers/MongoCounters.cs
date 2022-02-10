using Simple.Dotnet.Diagnostics.Actions;
using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Core.Handlers.Counters;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Streams.Streams;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public sealed class MongoCounters
{
    public static async Task<IResult> Handle(
        CountersQuery query,
        MongoConfig config,
        ActionsRegistry registry,
        ILogger logger)
    {
        var name = $"mongo-action-{Guid.NewGuid()}";
        var action = ActionTypes.NonStop(name, logger, () => new MongoStream(config), () =>
        {
            var subscriptionResult = Counters.Handle(ref query, CancellationToken.None); // Cancellation is not bound to a request
            if (subscriptionResult.IsOk) return subscriptionResult.Ok!;

            throw new Exception($"Failed to create counters subscription. Error: {subscriptionResult.Error.Validation ?? subscriptionResult.Error.Exception!.Message}", subscriptionResult.Error.Exception); // Rethrow and retry
        });

        var registerResult = await registry.Register(name, action);
        if (registerResult.IsOk)
        {
            // Fire and forget
            _ = registerResult.Ok!.ContinueWith(t =>
            {
                if (!t.Result.IsOk) logger.LogWarning(t.Result.Error, "An action completed with unsuccessful result. Action: {Action}", name);
            });
            return JsonResult.Create(ResponseMapper.ToResponse(UniResult.Ok<string, Exception>(name)));
        }

        logger.LogWarning(registerResult.Error, "Failed to schedule a kafka action, exception occurred");
        var response = ResponseMapper.ToResponse(registerResult, e => new(ErrorCodes.KafkaCountersFailed, "Failed to schedule an action"));

        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }
}