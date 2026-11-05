using NodaTime;

namespace TradingEngine.Application.PriceCollection;

public sealed record SessionWindow(
    IReadOnlySet<IsoDayOfWeek> Days,
    LocalTime Open,
    LocalTime Close);
