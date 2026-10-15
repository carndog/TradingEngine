using Microsoft.AspNetCore.Components;

namespace TradingEngine.Client.Services;

public static class AuthLinks
{
    public const string SignOutUrl = "/.auth/logout?post_logout_redirect_uri=/signin.html";

    public static string SignInUrl(string? returnTo)
    {
        string target = IsSafeReturnPath(returnTo) ? returnTo! : "/";
        return $"/.auth/login/aad?post_login_redirect_uri={Uri.EscapeDataString(target)}";
    }

    public static string SignInUrl(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        return SignInUrl("/" + navigation.ToBaseRelativePath(navigation.Uri));
    }

    public static bool IsSafeReturnPath(string? path)
    {
        return string.IsNullOrEmpty(path) is false
            && path![0] == '/'
            && (path.Length == 1 || (path[1] != '/' && path[1] != '\\'))
            && path.IndexOf(':') < 0;
    }
}
