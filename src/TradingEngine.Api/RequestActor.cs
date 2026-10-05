using System.Security.Claims;

namespace TradingEngine.Api;

internal static class RequestActor
{
    private const string UnverifiedLocalCaller = "unverified-local-caller";

    internal static string Resolve(HttpContext context)
    {
        ClaimsPrincipal user = context.User;
        string? identity = user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrWhiteSpace(identity)
            ? UnverifiedLocalCaller
            : identity;
    }
}
