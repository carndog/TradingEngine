using NodaTime;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.PriceCollection;

public sealed class WeeklyScheduleMarketSessionPolicy : IMarketSessionPolicy
{
    private readonly IReadOnlyDictionary<string, ExchangeSession> _sessions;

    public WeeklyScheduleMarketSessionPolicy(
        IReadOnlyDictionary<string, ExchangeSession> sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public ExchangeSessionStatus GetStatus(string exchange, Instant at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);

        if (_sessions.TryGetValue(exchange, out ExchangeSession? session) is false)
        {
            return ExchangeSessionStatus.Unconfigured;
        }

        if (session.AlwaysOpen)
        {
            return ExchangeSessionStatus.Open;
        }

        if (session.Zone is null)
        {
            return ExchangeSessionStatus.Unconfigured;
        }

        LocalDateTime local = at.InZone(session.Zone).LocalDateTime;
        IsoDayOfWeek day = local.DayOfWeek;
        IsoDayOfWeek previousDay = local.Date.PlusDays(-1).DayOfWeek;
        LocalTime time = local.TimeOfDay;

        foreach (SessionWindow window in session.Windows)
        {
            if (IsWithin(window, day, previousDay, time))
            {
                return ExchangeSessionStatus.Open;
            }
        }

        return ExchangeSessionStatus.Closed;
    }

    private static bool IsWithin(
        SessionWindow window,
        IsoDayOfWeek day,
        IsoDayOfWeek previousDay,
        LocalTime time)
    {
        if (window.Close > window.Open)
        {
            return window.Days.Contains(day)
                && time >= window.Open
                && time < window.Close;
        }

        return (window.Days.Contains(day) && time >= window.Open)
            || (window.Days.Contains(previousDay) && time < window.Close);
    }
}
