using NodaTime;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentRegistration(
    WatchedInstrumentFields Fields,
    MonitoringState MonitoringState,
    Instant CreatedAt,
    ChartAnalysisDefinition Definition);
