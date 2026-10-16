using TradingEngine.Api.Authentication;

namespace TradingEngine.Api;

internal static class RequestActor
{
    internal const string UnverifiedLocalCaller = "unverified-local-caller";

    internal static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return EasyAuthClientPrincipal.StableIdentifier(context.User) ?? UnverifiedLocalCaller;
    }

    internal static bool IsVerifiedMatch(string actor, string? createdBy)
    {
        return actor != UnverifiedLocalCaller
            && string.Equals(actor, createdBy, StringComparison.Ordinal);
    }
}
