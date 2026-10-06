using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace TradingEngine.Api.Authentication;

internal sealed class EasyAuthPrincipalMiddleware
{
    internal const string PrincipalRequiredCode = "authentication.principal_required";
    internal const string PrincipalMalformedCode = "authentication.principal_malformed";
    internal const string PrincipalIdentifierMissingCode = "authentication.principal_identifier_missing";

    private readonly RequestDelegate _next;
    private readonly bool _trustPlatformHeaders;

    public EasyAuthPrincipalMiddleware(RequestDelegate next, IOptions<EasyAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _next = next ?? throw new ArgumentNullException(nameof(next));
        _trustPlatformHeaders = options.Value.TrustPlatformHeaders;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_trustPlatformHeaders is false)
        {
            await _next(context);
            return;
        }

        string? header = context.Request.Headers[EasyAuthClientPrincipal.HeaderName];
        if (string.IsNullOrWhiteSpace(header))
        {
            await Reject(
                context,
                PrincipalRequiredCode,
                "The platform authentication principal header is missing.");
            return;
        }

        ClaimsPrincipal? principal = EasyAuthClientPrincipal.Decode(header);
        if (principal is null)
        {
            await Reject(
                context,
                PrincipalMalformedCode,
                "The platform authentication principal header could not be decoded.");
            return;
        }

        if (EasyAuthClientPrincipal.StableIdentifier(principal) is null)
        {
            await Reject(
                context,
                PrincipalIdentifierMissingCode,
                "The authenticated principal carries no stable object or subject identifier.");
            return;
        }

        context.User = principal;
        await _next(context);
    }

    private static Task Reject(HttpContext context, string code, string detail)
    {
        return ApiProblemDetails.Unauthorized(code, detail).ExecuteAsync(context);
    }
}
