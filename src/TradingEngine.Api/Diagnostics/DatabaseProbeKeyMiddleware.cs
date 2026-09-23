namespace TradingEngine.Api.Diagnostics;

internal sealed class DatabaseProbeKeyMiddleware
{
    public const string ProbeKeyHeader = "X-Database-Probe-Key";

    private readonly RequestDelegate _next;
    private readonly string? _probeKey;

    public DatabaseProbeKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _probeKey = configuration["Diagnostics:DatabaseProbeKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_probeKey) ||
            string.Equals(context.Request.Headers[ProbeKeyHeader].ToString(), _probeKey, StringComparison.Ordinal) is false)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }
}
