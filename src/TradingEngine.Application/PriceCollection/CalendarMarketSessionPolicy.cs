using NodaTime;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.PriceCollection;

public sealed class CalendarMarketSessionPolicy : IMarketSessionPolicy
{
    private readonly IReadOnlyDictionary<string, ExchangeSession> _sessions;
    private readonly WeeklyScheduleMarketSessionPolicy _weekly;
    private readonly IExchangeTradingCalendar _calendar;

    public CalendarMarketSessionPolicy(
        IReadOnlyDictionary<string, ExchangeSession> sessions,
        IExchangeTradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(calendar);
        _sessions = sessions;
        _weekly = new WeeklyScheduleMarketSessionPolicy(sessions);
        _calendar = calendar;
    }

    public ExchangeSessionStatus GetStatus(string exchange, Instant at)
    {
        return GetDecision(exchange, at).Status;
    }

    public MarketSessionDecision GetDecision(string exchange, Instant at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        if (_sessions.TryGetValue(exchange, out ExchangeSession? session) is false
            || (session.AlwaysOpen is false && session.Zone is null))
        {
            return MarketSessionDecision.FromStatus(ExchangeSessionStatus.Unconfigured);
        }

        if (session.AlwaysOpen || session.CalendarRequired is false)
        {
            return MarketSessionDecision.FromStatus(_weekly.GetStatus(exchange, at));
        }

        LocalDateTime local = at.InZone(session.Zone!).LocalDateTime;
        ExchangeTradingDay day = _calendar.GetDay(exchange, local.Date);
        if (day.Kind == TradingDayKind.Unavailable || day.TimeZone != session.Zone!.Id)
        {
            return new MarketSessionDecision(ExchangeSessionStatus.CalendarUnavailable,
                "price_collection.calendar_unavailable",
                day.TimeZone != session.Zone!.Id && day.Kind != TradingDayKind.Unavailable
                    ? "The calendar and exchange session time zones disagree." : day.Reason,
                day.Version, day.Notice);
        }

        if (day.Kind == TradingDayKind.Closed)
        {
            return new MarketSessionDecision(ExchangeSessionStatus.Closed,
                day.Reason == "exceptional_closure" ? "price_collection.exceptional_closure" : "price_collection.holiday",
                $"The exchange is closed: {day.Reason}.", day.Version, day.Notice);
        }

        if (day.Kind == TradingDayKind.Replacement)
        {
            bool open = day.Windows?.Any(window => local.TimeOfDay >= window.Open && local.TimeOfDay < window.Close) == true;
            return new MarketSessionDecision(open ? ExchangeSessionStatus.Open : ExchangeSessionStatus.Closed,
                open ? null : "price_collection.outside_replacement_session",
                open ? null : $"Outside the exchange's replacement session: {day.Reason}.", day.Version, day.Notice);
        }

        MarketSessionDecision weekly = MarketSessionDecision.FromStatus(_weekly.GetStatus(exchange, at));
        return weekly with { CalendarVersion = day.Version, Notice = day.Notice };
    }
}
