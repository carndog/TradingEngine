namespace TradingEngine.Infrastructure.MarketSessions;

public sealed class ExchangeCalendarOptions
{
    public string Exchange { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string TimeZone { get; set; } = string.Empty;

    public string CoverageStart { get; set; } = string.Empty;

    public string CoverageEnd { get; set; } = string.Empty;

    public string VerifiedOn { get; set; } = string.Empty;

    public List<string> Sources { get; set; } = [];

    public List<CalendarDayOptions> Exceptions { get; set; } = [];

    public List<CalendarDayOptions> Overrides { get; set; } = [];
}
