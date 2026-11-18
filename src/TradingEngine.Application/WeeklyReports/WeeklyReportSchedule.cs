using NodaTime;
using NodaTime.Text;

namespace TradingEngine.Application.WeeklyReports;

public sealed class WeeklyReportSchedule
{
    private readonly DateTimeZone _zone;
    private readonly IsoDayOfWeek _day;
    private readonly LocalTime _time;

    public WeeklyReportSchedule(WeeklyReportOptions options)
    {
        options.Validate();
        _zone = DateTimeZoneProviders.Tzdb[options.TimeZone];
        _day = (IsoDayOfWeek)options.IsoDayOfWeek;
        _time = LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(options.LocalTime).Value;
        Fingerprint = $"{options.TimeZone}|{options.IsoDayOfWeek}|{options.LocalTime}";
    }

    public string Fingerprint { get; }

    public Instant NextAfter(Instant now)
    {
        LocalDate date = now.InZone(_zone).Date;
        int days = ((int)_day - (int)date.DayOfWeek + 7) % 7;
        Instant candidate = At(date.PlusDays(days));
        return candidate > now ? candidate : At(date.PlusDays(days + 7));
    }

    public Instant LatestOnOrBefore(Instant now)
    {
        LocalDate date = now.InZone(_zone).Date;
        int days = ((int)date.DayOfWeek - (int)_day + 7) % 7;
        Instant candidate = At(date.PlusDays(-days));
        return candidate <= now ? candidate : At(date.PlusDays(-days - 7));
    }

    public WeeklyReportPeriod PeriodEndingAt(Instant cutoff)
    {
        LocalDate date = cutoff.InZone(_zone).Date;
        return new WeeklyReportPeriod(At(date.PlusDays(-7)), cutoff, At(date.PlusDays(7)));
    }

    public int SkippedPeriods(Instant firstDue, Instant latestDue)
    {
        int days = Period.Between(firstDue.InZone(_zone).Date, latestDue.InZone(_zone).Date, PeriodUnits.Days).Days;
        return Math.Max(0, days / 7);
    }

    private Instant At(LocalDate date)
    {
        return _zone.AtLeniently(date.At(_time)).ToInstant();
    }
}
