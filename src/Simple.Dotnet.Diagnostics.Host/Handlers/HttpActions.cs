using Simple.Dotnet.Diagnostics.Actions.Registry;
using Simple.Dotnet.Diagnostics.Host.AspNetCore;
using Simple.Dotnet.Diagnostics.Interceptors;
using Simple.Dotnet.Diagnostics.Interceptors.AspNetCore;

namespace Simple.Dotnet.Diagnostics.Host.Handlers;

public readonly record struct GetAllActionsQuery();

public readonly record struct CancelActionCommand(string ActionName);

public sealed class HttpActions
{
    public static async Task<IResult> GetAll(GetAllActionsQuery query, ILogger logger, ActionsRegistry registry, CancellationToken token)
    {
        var result = await registry.GetActions();
        if (result.IsOk) return JsonResult.Create(result);

        logger.LogWarning(result.Error, "Failed to get actions from registry");

        var response = ResponseMapper.ToResponse(result, e => new(ErrorCodes.GetActionsFailed, "Failed to get actions from registry"));
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }

    public static async Task<IResult> Cancel(CancelActionCommand cmd, ILogger logger, ActionsRegistry registry, CancellationToken token)
    {
        var result = await registry.Cancel(cmd.ActionName);
        if (result.IsOk) return JsonResult.Create(result);

        logger.LogWarning(result.Error, "Failed to cancel action with name: {ActionName}", cmd.ActionName);

        var response = ResponseMapper.ToResponse(result, e => new(ErrorCodes.GetActionsFailed, "Failed to cancel action"));
        return JsonResult.Create(response, ErrorCodes.ToHttpCode(response.Error!.Value.Code));
    }
}