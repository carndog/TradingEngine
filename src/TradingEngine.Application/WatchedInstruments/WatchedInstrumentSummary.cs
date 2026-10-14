using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentSummary(
    Guid Id,
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    MonitoringState MonitoringState,
    int SamplingIntervalSeconds);
