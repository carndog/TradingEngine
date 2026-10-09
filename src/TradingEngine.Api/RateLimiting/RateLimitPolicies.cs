using TradingEngine.Api.Authentication;

namespace TradingEngine.Api.RateLimiting;

internal static class RateLimitPolicies
{
    internal const string AdminRead = "admin-read";
    internal const string AdminWrite = "admin-write";
    internal const string DatabaseProbe = "database-probe";

    internal const string RateLimit = "rate";
    internal const string ConcurrencyLimit = "concurrency";
    internal const string NoLimit = "none";

    internal const string UnverifiedCallerPartition = "unverified";
    internal const string DatabaseProbePath = "/health/database";

    internal static string ForRequest(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Request.Path.StartsWithSegments(DatabaseProbePath)
            ? DatabaseProbe
            : ForAdminRequest(context.Request);
    }

    internal static string ForAdminRequest(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return IsWrite(request.Method) ? AdminWrite : AdminRead;
    }

    internal static bool IsWrite(string method)
    {
        return HttpMethods.IsGet(method) is false && HttpMethods.IsHead(method) is false;
    }

    internal static string ResolveCaller(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? identifier = context.User is null
            ? null
            : EasyAuthClientPrincipal.StableIdentifier(context.User);

        return identifier ?? UnverifiedCallerPartition;
    }
}
