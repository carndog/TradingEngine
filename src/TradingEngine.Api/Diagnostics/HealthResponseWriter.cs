using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.Diagnostics;

internal static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        HealthResponse response = new(report.Status.ToString());
        return context.Response.WriteAsJsonAsync(response);
    }
}
