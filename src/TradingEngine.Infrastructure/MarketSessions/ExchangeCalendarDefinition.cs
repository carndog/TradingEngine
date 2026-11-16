using NodaTime;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Infrastructure.MarketSessions;

internal sealed record ExchangeCalendarDefinition(
    string Version,
    string TimeZone,
    LocalDate Start,
    LocalDate End,
    IReadOnlyDictionary<LocalDate, ExchangeTradingDay> Days);
