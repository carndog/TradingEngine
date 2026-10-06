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
}
