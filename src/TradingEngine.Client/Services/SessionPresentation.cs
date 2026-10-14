using TradingEngine.Contracts.Session;

namespace TradingEngine.Client.Services;

public sealed record SessionPresentation(
    SessionStatus Status,
    string Label,
    string? SignInUrl,
    string? SignOutUrl,
    bool CanRetry)
{
    public static SessionPresentation Checking { get; } = new(
        SessionStatus.Checking,
        "Checking session…",
        null,
        null,
        false);

    public static SessionPresentation From(ApiResult<SessionResponse> result, string signInUrl)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return result.Value!.Mode switch
            {
                "entra" => new SessionPresentation(
                    SessionStatus.SignedIn,
                    SignedInLabel(result.Value.DisplayName),
                    null,
                    AuthLinks.SignOutUrl,
                    false),
                "development" => new SessionPresentation(
                    SessionStatus.Development,
                    "Local development",
                    null,
                    null,
                    false),
                _ => Unavailable()
            };
        }

        ApiError error = result.Error!;
        if (error.IsUnauthorized)
        {
            return new SessionPresentation(
                SessionStatus.Unauthorized,
                "Sign in required",
                signInUrl,
                null,
                false);
        }

        if (error.IsForbidden)
        {
            return new SessionPresentation(
                SessionStatus.Forbidden,
                "Access denied",
                null,
                AuthLinks.SignOutUrl,
                false);
        }

        return Unavailable();
    }

    private static SessionPresentation Unavailable()
    {
        return new SessionPresentation(
            SessionStatus.Unavailable,
            "Session check unavailable",
            null,
            null,
            true);
    }

    private static string SignedInLabel(string? displayName)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? "Signed in"
            : $"Signed in as {displayName.Trim()}";
    }
}
