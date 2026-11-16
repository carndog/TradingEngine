namespace TradingEngine.Infrastructure.MarketSessions;

public sealed class CalendarDayOptions
{
    public string Date { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public bool Closed { get; set; }

    public List<CalendarWindowOptions> Windows { get; set; } = [];
}
