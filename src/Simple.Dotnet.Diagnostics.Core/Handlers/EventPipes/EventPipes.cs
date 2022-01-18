using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Threading;

using SubscriptionTask = System.Threading.Tasks.Task<Simple.Dotnet.Utilities.Results.UniResult<Simple.Dotnet.Utilities.Results.Unit, System.Exception>>;

namespace Simple.Dotnet.Diagnostics.Core.Handlers.EventPipes;

public readonly record struct SubscribeCommand(int ProcessId, EventPipeProvider[] Providers);

public static class EventPipes
{
    public static Result<SubscriptionTask, DiagnosticsError> Handle(in SubscribeCommand command, Action<EventMetric> sideEffect, CancellationToken token)
    {
        if (command.Providers is null or { Length: 0 }) 
            return Result.Error<SubscriptionTask, DiagnosticsError>(new($"Can't create an event subscription for an empty {nameof(SubscribeCommand.Providers)} array"));

        if (command.ProcessId < 0) 
            return Result.Error<SubscriptionTask, DiagnosticsError>(new($"Can't create an event subscription for a negative {nameof(SubscribeCommand.ProcessId)}"));
        
        try
        {
            var client = new DiagnosticsClient(command.ProcessId);
            var session = client.StartEventPipeSession(command.Providers, false);

            return Result.Ok<SubscriptionTask, DiagnosticsError>(new Subscription(session, new(session.EventStream), sideEffect, token).Start());
        }
        catch (Exception ex)
        {
            return Result.Error<SubscriptionTask, DiagnosticsError>(new(ex));
        }
    }
}
