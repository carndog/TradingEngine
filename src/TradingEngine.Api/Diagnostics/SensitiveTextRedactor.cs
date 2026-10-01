using System.Text.RegularExpressions;

namespace TradingEngine.Api.Diagnostics;

internal static class SensitiveTextRedactor
{
    internal const string RedactedMarker = "<redacted>";

    private static readonly Regex CredentialPairPattern = new(
        @"\b(password|pwd|instrumentationkey|sharedaccesskey|accountkey|apikey|api[_-]?key|x-database-probe-key|authorization|cookie|set-cookie|connectionstring|secret|sig|token)(\s*[:=]\s*)(?!bearer\b)(""[^""]*""|'[^']*'|[^\s;""']+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"\bbearer\s+\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChartAnalysisDocumentPattern = new(
        @"<\s*ChartAnalysisDefinition\b[\s\S]*?(<\s*/\s*ChartAnalysisDefinition\s*>|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SensitiveKeyFragments =
    [
        "authorization",
        "cookie",
        "token",
        "secret",
        "password",
        "apikey",
        "api-key",
        "x-database-probe-key",
        "connectionstring",
        "instrumentationkey"
    ];

    public static bool ContainsSensitiveValue(string value)
    {
        return CredentialPairPattern.IsMatch(value)
            || BearerPattern.IsMatch(value)
            || ChartAnalysisDocumentPattern.IsMatch(value);
    }

    public static bool ContainsSensitiveValue(
        string value,
        IReadOnlyCollection<string> sensitiveValues)
    {
        if (ContainsSensitiveValue(value))
        {
            return true;
        }

        foreach (string sensitiveValue in sensitiveValues)
        {
            if (value.Contains(sensitiveValue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsSensitiveKey(string key)
    {
        foreach (string fragment in SensitiveKeyFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string Redact(string value)
    {
        string redacted = BearerPattern.Replace(value, $"Bearer {RedactedMarker}");
        redacted = CredentialPairPattern.Replace(
            redacted,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}{RedactedMarker}");
        redacted = ChartAnalysisDocumentPattern.Replace(redacted, "<redacted chart-analysis document>");

        return redacted;
    }

    public static string Redact(string value, IReadOnlyCollection<string> sensitiveValues)
    {
        string redacted = value;
        foreach (string sensitiveValue in sensitiveValues)
        {
            redacted = redacted.Replace(sensitiveValue, RedactedMarker, StringComparison.Ordinal);
        }

        return Redact(redacted);
    }
}
