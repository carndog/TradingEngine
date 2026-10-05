using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Contracts.MonitoringRules;

public sealed record MonitoringRuleDefinitionDto(
    int PriceScale,
    IReadOnlyList<ChartZoneDto>? SupportZones,
    IReadOnlyList<ChartZoneDto>? ResistanceZones);
