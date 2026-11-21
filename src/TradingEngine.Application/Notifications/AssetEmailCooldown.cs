using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace TradingEngine.Application.Notifications;

public sealed class AssetEmailCooldown
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };

    private readonly Period? _default;
    private readonly Dictionary<Guid, Period> _assets = [];
    private readonly Dictionary<Guid, List<(decimal From, decimal? To, Period Interval)>> _bands = [];

    private AssetEmailCooldown(Period? defaultPeriod)
    {
        _default = defaultPeriod;
    }

    public static AssetEmailCooldown Disabled { get; } = new(null);

    public bool Enabled => _default is not null;

    public static AssetEmailCooldown FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Disabled;
        }

        AssetEmailCooldownSettings? settings = JsonSerializer.Deserialize<AssetEmailCooldownSettings>(json, JsonOptions);
        if (settings is null)
        {
            throw new ArgumentException("Email cooldown settings must be a JSON object.");
        }

        AssetEmailCooldown policy = new(ParsePeriod(settings.DefaultPeriod));
        foreach (AssetEmailCooldownOverride entry in settings.Overrides ?? [])
        {
            policy.AddOverride(entry);
        }

        return policy;
    }

    public bool IsCoolingDown(Guid instrumentId, decimal alertPrice, Instant lastAlert, Instant crossing)
    {
        if (_default is null)
        {
            return false;
        }

        if (crossing < lastAlert)
        {
            return true;
        }

        Period interval = Resolve(instrumentId, alertPrice, _default);
        if (interval.Months != 0)
        {
            Period elapsed = Period.Between(lastAlert.InUtc().LocalDateTime, crossing.InUtc().LocalDateTime,
                PeriodUnits.Months);
            return elapsed.Months < interval.Months;
        }

        return crossing - lastAlert < interval.ToDuration();
    }

    private Period Resolve(Guid instrumentId, decimal price, Period fallback)
    {
        if (_bands.TryGetValue(instrumentId, out List<(decimal From, decimal? To, Period Interval)>? bands))
        {
            foreach ((decimal from, decimal? to, Period interval) in bands)
            {
                if (price >= from && (to is null || price < to))
                {
                    return interval;
                }
            }
        }

        return _assets.GetValueOrDefault(instrumentId, fallback);
    }

    private void AddOverride(AssetEmailCooldownOverride entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.InstrumentId == Guid.Empty)
        {
            throw new ArgumentException("A cooldown override requires an instrument ID.");
        }

        Period interval = ParsePeriod(entry.Period);
        if (entry.FromInclusive is null && entry.ToExclusive is null)
        {
            if (_assets.TryAdd(entry.InstrumentId, interval) is false)
            {
                throw new ArgumentException("An instrument has multiple default intervals.");
            }

            return;
        }

        decimal from = entry.FromInclusive ?? 0m;
        decimal? to = entry.ToExclusive;
        if (from < 0 || to <= from)
        {
            throw new ArgumentException("A price band requires ordered nonnegative bounds.");
        }

        if (_bands.TryGetValue(entry.InstrumentId, out List<(decimal From, decimal? To, Period Interval)>? bands) is false)
        {
            bands = [];
            _bands.Add(entry.InstrumentId, bands);
        }

        if (bands.Any(band => (to is null || band.From < to) && (band.To is null || from < band.To)))
        {
            throw new ArgumentException("Price bands for the same instrument overlap.");
        }

        bands.Add((from, to, interval));
    }

    private static Period ParsePeriod(string? value)
    {
        ParseResult<Period> parsed = PeriodPattern.Roundtrip.Parse(value ?? string.Empty);
        if (parsed.Success)
        {
            Period period = parsed.Value;
            if ((period.Months > 0 && period == Period.FromMonths(period.Months))
                || (period.Days > 0 && period == Period.FromDays(period.Days))
                || (period.Hours > 0 && period == Period.FromHours(period.Hours))
                || (period.Minutes > 0 && period == Period.FromMinutes(period.Minutes)))
            {
                if (period.Months == 0)
                {
                    _ = period.ToDuration();
                }

                return period;
            }
        }

        throw new ArgumentException("Use a positive ISO period in calendar months, days, hours or minutes.");
    }
}
