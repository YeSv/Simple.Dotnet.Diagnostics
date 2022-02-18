using Microsoft.Diagnostics.NETCore.Client;
using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Core.Handlers;

public readonly record struct SubscribeCommand(int ProcessId, EventPipeProvider[] Providers);

public static class EventPipes
{
    public static Result<EventPipeSession, DiagnosticsError> Handle(in SubscribeCommand command, CancellationToken token)
    {
        if (command.Providers is null or { Length: 0 }) 
            return Result.Error<EventPipeSession, DiagnosticsError>(new($"Can't create an event subscription for an empty {nameof(SubscribeCommand.Providers)} array"));

        if (command.ProcessId < 0) 
            return Result.Error<EventPipeSession, DiagnosticsError>(new($"Can't create an event subscription for a negative {nameof(SubscribeCommand.ProcessId)}"));
        
        try
        {
            var client = new DiagnosticsClient(command.ProcessId);
            var session = client.StartEventPipeSession(command.Providers, false);

            return Result.Ok<EventPipeSession, DiagnosticsError>(session);
        }
        catch (Exception ex)
        {
            return Result.Error<EventPipeSession, DiagnosticsError>(new(ex));
        }
    }
}
