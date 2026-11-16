using NodaTime;

namespace TradingEngine.Application.PriceCollection;

public sealed record TradingDayWindow(LocalTime Open, LocalTime Close);
