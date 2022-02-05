using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Simple.Dotnet.Diagnostics.Host.AspNetCore.Health;

public static class HealthFormatter
{
    public static Task WriteResponse(HttpContext context, HealthReport healthReport) =>
        JsonResult.Create(healthReport, healthReport.Status switch
        {
            HealthStatus.Healthy => StatusCodes.Status200OK,
            HealthStatus.Degraded => StatusCodes.Status400BadRequest,
            HealthStatus.Unhealthy => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        }).ExecuteAsync(context);
}
