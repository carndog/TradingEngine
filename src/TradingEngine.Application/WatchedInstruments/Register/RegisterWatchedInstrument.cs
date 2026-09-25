using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed record RegisterWatchedInstrument(
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    int SamplingIntervalSeconds,
    MonitoringState MonitoringState,
    ChartAnalysisDefinition Definition);
