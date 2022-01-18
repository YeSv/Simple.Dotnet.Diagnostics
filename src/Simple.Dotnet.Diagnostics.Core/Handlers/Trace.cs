using Simple.Dotnet.Utilities.Results;
using System.Threading;
using System.Threading.Tasks;

namespace Simple.Dotnet.Diagnostics.Core.Handlers;

public readonly record struct TraceQuery(int? ClrEventLevel, int? ClrEvents, string? Format, string? Providers, int? ProcessId, string? ProcessName, string? Profile);

public static class Trace
{
    public static ValueTask<Result<Unit, DiagnosticsError>> Handle(ref TraceQuery query, CancellationToken token)
    {
        if (query.ProcessId is < 0) 
            return Result.Error<Unit, DiagnosticsError>(new("Query contains not valid process id")).AsValueTask();

        if (!query.ProcessId.HasValue && string.IsNullOrWhiteSpace(query.ProcessName)) 
            return Result.Error<Unit, DiagnosticsError>(new("Process name or process id should be specified")).AsValueTask();

        if (!string.IsNullOrWhiteSpace(query.ProcessName))
        {
            var result = Processes.Handle(new GetProcessByNameQuery(query.ProcessName!), token);
            if (!result.IsOk) return Result.Error<Unit, DiagnosticsError>(result.Error).AsValueTask();
            query = query with { ProcessId = result.Ok.ProcessId };
        }

        var idLookup = Processes.Handle(new GetProcessByIdQuery(query.ProcessId!.Value), token);
        if (!idLookup.IsOk) return Result.Error<Unit, DiagnosticsError>(idLookup.Error).AsValueTask();
        
        // TODO: implement
        return Result.Ok<Unit, DiagnosticsError>(Unit.Shared).AsValueTask();
    }
}
