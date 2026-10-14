using TradingEngine.Domain.Results;

namespace TradingEngine.Api;

internal static class ConcurrencyTokens
{
    internal static void SetConcurrencyHeaders(HttpContext context, byte[] token)
    {
        context.Response.Headers.ETag = $"\"{FormatToken(token)}\"";
    }

    internal static string FormatToken(byte[] token)
    {
        return Convert.ToBase64String(token);
    }

    internal static (byte[]? Token, Error? Error) ExpectedToken(
        HttpRequest request,
        Error malformedTokenError)
    {
        string? header = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return (null, null);
        }

        string value = header.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value.Substring(2).Trim();
        }

        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value.Substring(1, value.Length - 2);
        }

        try
        {
            byte[] decoded = Convert.FromBase64String(value);
            return decoded.Length == 0
                ? (null, malformedTokenError)
                : (decoded, null);
        }
        catch (FormatException)
        {
            return (null, malformedTokenError);
        }
    }
}
