using NodaTime;

namespace TradingEngine.Application.PriceCollection;

public sealed record ExchangeSession(
    DateTimeZone? Zone,
    bool AlwaysOpen,
    IReadOnlyList<SessionWindow> Windows);
