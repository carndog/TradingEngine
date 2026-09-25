namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record RegisterWatchedInstrumentRequest(
    string? Symbol,
    string? Exchange,
    string? QuoteCurrency,
    int SamplingIntervalSeconds,
    string? MonitoringState,
    int PriceScale,
    IReadOnlyList<ChartZoneDto>? SupportZones,
    IReadOnlyList<ChartZoneDto>? ResistanceZones);
