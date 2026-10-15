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
        if (value is null)
        {
            return string.Empty;
        }

        DateTime utc = value.Value.UtcDateTime;
        long truncatedTicks = utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond;
        string format = truncatedTicks % TimeSpan.TicksPerSecond == 0
            ? "yyyy-MM-dd'T'HH:mm:ss"
            : "yyyy-MM-dd'T'HH:mm:ss.fff";
        return new DateTime(truncatedTicks, DateTimeKind.Utc)
            .ToString(format, CultureInfo.InvariantCulture);
    }

    public static bool HasSubMillisecondTicks(DateTimeOffset? value)
    {
        return value is not null
            && value.Value.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond != 0;
    }

    public static bool TryParseInput(string? value, out DateTimeOffset? result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            return true;
        }

        bool parsed = DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsedValue);

        result = parsed ? parsedValue.ToUniversalTime() : null;
        return parsed;
    }

    public static string? ToIsoUtc(DateTimeOffset? value)
    {
        return value?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }
}
