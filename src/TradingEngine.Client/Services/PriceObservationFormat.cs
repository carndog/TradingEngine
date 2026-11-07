using System.Globalization;

namespace TradingEngine.Client.Services;

public static class PriceObservationFormat
{
    public const string Missing = "—";
    public const string MajorDisplayUnit = "major";
    public const string NativeDisplayUnit = "native";
    public const int MinimumDecimalPlaces = 0;
    public const int MaximumDecimalPlaces = 8;

    private static readonly IReadOnlyDictionary<string, FixedUnitConversion> Conversions =
        new Dictionary<string, FixedUnitConversion>(StringComparer.Ordinal)
        {
            ["GBX"] = new FixedUnitConversion("GBP", 100m)
        };

    public static bool HasMajorUnit(string storedUnit)
    {
        return Conversions.ContainsKey(storedUnit);
    }

    public static string MajorUnitOf(string storedUnit)
    {
        return Conversions.TryGetValue(storedUnit, out FixedUnitConversion? conversion)
            ? conversion.MajorUnit
            : storedUnit;
    }

    public static string DisplayUnit(string storedUnit, string displayPreference)
    {
        return displayPreference == NativeDisplayUnit
            ? storedUnit
            : MajorUnitOf(storedUnit);
    }

    public static decimal? ToDisplayUnit(decimal? value, string storedUnit, string displayUnit)
    {
        if (value is null)
        {
            return null;
        }

        return Conversions.TryGetValue(storedUnit, out FixedUnitConversion? conversion)
            && conversion.MajorUnit == displayUnit
                ? value.Value / conversion.SubunitsPerMajor
                : value.Value;
    }

    public static string FormatPrice(decimal? value, int decimalPlaces)
    {
        if (value is null)
        {
            return Missing;
        }

        int places = Math.Clamp(decimalPlaces, MinimumDecimalPlaces, MaximumDecimalPlaces);
        decimal rounded = Math.Round(value.Value, places, MidpointRounding.AwayFromZero);

        return rounded.ToString("N" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static string FormatPrice(decimal? value, string storedUnit, string displayUnit, int decimalPlaces)
    {
        return FormatPrice(ToDisplayUnit(value, storedUnit, displayUnit), decimalPlaces);
    }

    public static string FormatDate(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("dd MMM yyyy',' ", CultureInfo.InvariantCulture).TrimEnd();
    }

    public static string FormatClock(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public static string FormatMinute(DateTimeOffset value)
    {
        return $"{FormatDate(value)} {FormatClock(value)}";
    }

    public static string FormatTimeliness(string timeliness)
    {
        return timeliness switch
        {
            "realtime" => "Real-time",
            "delayed" => "Delayed",
            "unknown" => "Unknown",
            _ => timeliness
        };
    }

    public static string FormatCursor(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }
}
