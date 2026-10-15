using System.Security.Claims;
using Microsoft.Extensions.Options;
using TradingEngine.Api.Authentication;
using TradingEngine.Contracts.Session;

namespace TradingEngine.Api.Session;

internal static class SessionEndpoints
{
    internal const string ModeDevelopment = "development";
    internal const string ModeEntra = "entra";
    internal const string SessionRequiredCode = "authentication.session_required";

    private const string NameClaimType = "name";
    private const string PreferredUsernameClaimType = "preferred_username";

    public static RouteGroupBuilder MapSessionEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup("/api/session");
        group.MapGet("", GetAsync);

        return group;
    }

    private static IResult GetAsync(HttpContext context, IOptions<EasyAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Value.TrustPlatformHeaders is false)
        {
            return TypedResults.Ok(new SessionResponse(ModeDevelopment, null));
        }

        if (context.User.Identity?.IsAuthenticated is not true)
        {
            return ApiProblemDetails.Unauthorized(
                SessionRequiredCode,
                "An authenticated session is required.");
        }

        string? displayName = context.User.FindFirst(NameClaimType)?.Value
            ?? context.User.FindFirst(PreferredUsernameClaimType)?.Value
            ?? context.User.FindFirst(ClaimTypes.Name)?.Value;

        return TypedResults.Ok(new SessionResponse(ModeEntra, displayName));
    }
}
