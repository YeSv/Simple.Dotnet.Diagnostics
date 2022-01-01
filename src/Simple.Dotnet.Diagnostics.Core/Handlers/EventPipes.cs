using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Diagnostics.Core.Domains.EventPipes;
using Simple.Dotnet.Utilities.Results;
using System;
using System.Threading;

namespace Simple.Dotnet.Diagnostics.Core.Handlers;

public readonly record struct CreateSubscriptionCommand(int ProcessId, EventPipeProvider[] Providers);

public static class EventPipes
{
    public static Result<EventsSubscription, DiagnosticsError> Handle(in CreateSubscriptionCommand command, CancellationToken token)
    {
        if (command.Providers is null or { Length: 0 }) return Result.Error<EventsSubscription, DiagnosticsError>(new($"Can't create an event subscription for an empty {nameof(CreateSubscriptionCommand.Providers)} array"));
        if (command.ProcessId < 0) return Result.Error<EventsSubscription, DiagnosticsError>(new($"Can't create an event subscription for a negative {nameof(CreateSubscriptionCommand.ProcessId)}"));
        
        try
        {
            var client = new DiagnosticsClient(command.ProcessId);
            var session = client.StartEventPipeSession(command.Providers, false);

            return Result.Ok<EventsSubscription, DiagnosticsError>(new(session, new(session.EventStream)));
        }
        catch (Exception ex)
        {
            return Result.Error<EventsSubscription, DiagnosticsError>(new(ex));
        }
    }
}
