using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentConfiguration(
    WatchedInstrument Instrument,
    ChartAnalysisDefinition Definition);
