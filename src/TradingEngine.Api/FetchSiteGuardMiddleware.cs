namespace TradingEngine.Api;

internal sealed class FetchSiteGuardMiddleware
{
    internal const string CrossSiteWriteCode = "request.cross_site_write_rejected";

    private readonly RequestDelegate _next;

    public FetchSiteGuardMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (RateLimiting.RateLimitPolicies.IsWrite(context.Request.Method) is false)
        {
            await _next(context);
            return;
        }

        string? site = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (string.IsNullOrWhiteSpace(site)
            || string.Equals(site, "same-origin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(site, "none", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        await ApiProblemDetails.Forbidden(
            CrossSiteWriteCode,
            "Writes initiated by another site are rejected.").ExecuteAsync(context);
    }
}
