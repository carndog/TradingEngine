using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed record RegisterWatchedInstrument(
    Guid Id,
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    int SamplingIntervalSeconds,
    MonitoringState MonitoringState,
    ChartAnalysisDefinition Definition);
