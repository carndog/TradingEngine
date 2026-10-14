using System.Globalization;

namespace TradingEngine.Client.Services;

public static class UtcTimes
{
    public static string Format(DateTimeOffset? value)
    {
        return value is null
            ? "No end set"
            : Format(value.Value);
    }

    public static string Format(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
    }

    public static string FormatPrecise(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString(
            "dd MMM yyyy, HH:mm:ss.fffffff 'UTC'",
            CultureInfo.InvariantCulture);
    }

    public static string FormatRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        return $"{Format(from)} → {(to is null ? "No end set" : Format(to.Value))}";
    }

    public static string ToInputValue(DateTimeOffset? value)
    {
        return value?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    public static DateTimeOffset? FromInputValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    public static string? ToIsoUtc(DateTimeOffset? value)
    {
        return value?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    public static bool IsFuture(DateTimeOffset? value)
    {
        return value is not null && value.Value.UtcDateTime > DateTimeOffset.UtcNow.UtcDateTime;
    }

    public static bool HasEnded(DateTimeOffset? value)
    {
        return value is not null && value.Value.UtcDateTime <= DateTimeOffset.UtcNow.UtcDateTime;
    }
}
