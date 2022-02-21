using Simple.Dotnet.Diagnostics.Actions;
using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Interceptors;
using Simple.Dotnet.Diagnostics.Interceptors.AspNetCore;
using Simple.Dotnet.Utilities.Results;
using System.Linq;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public readonly record struct GetAllActionsQuery();

public readonly record struct CancelActionCommand(string ActionName);

public readonly record struct ActionInfo(string Name, ActionHealthResult Health);

public sealed class HttpActions
{
    public static async Task<IResult> GetAll(GetAllActionsQuery query, ILogger logger, ActionsRegistry registry, CancellationToken token)
    {
        var result = await registry.GetActions();
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(UniResult.Ok<ActionInfo[], Exception>(result.Ok switch
        {
            null or { Length: 0} => Array.Empty<ActionInfo>(),
            var infos => infos.Select(h => new ActionInfo(h.Name, h.Health)).ToArray()
        }), default));

        logger.LogWarning(result.Error, "Failed to get actions from registry");

        var response = ResponseMapper.ToResponse(result, e => new(ErrorCodes.GetActionsFailed, "Failed to get actions from registry"));
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static async Task<IResult> Cancel(CancelActionCommand cmd, ILogger logger, ActionsRegistry registry, CancellationToken token)
    {
        var result = await registry.Cancel(cmd.ActionName);
        if (result.IsOk) return JsonResult.Create(ResponseMapper.ToResponse(result, default));

        logger.LogWarning(result.Error, "Failed to cancel action with name: {ActionName}", cmd.ActionName);

        var response = ResponseMapper.ToResponse(result, e => new(ErrorCodes.GetActionsFailed, "Failed to cancel action"));
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }
}