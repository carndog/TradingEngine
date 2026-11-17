using NodaTime;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Domain.SignalTracking;

public sealed record SignalPriceBaseline(
    Instant TriggeredAt,
    decimal TriggerPrice,
    PriceBasis PriceBasis,
    string? QuoteCurrency,
    decimal Lower,
    decimal Upper);
