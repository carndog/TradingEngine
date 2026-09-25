namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record WatchedInstrumentResponse(
    Guid Id,
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    string MonitoringState,
    int SamplingIntervalSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastChangedAt,
    int PriceScale,
    IReadOnlyList<ChartZoneDto> SupportZones,
    IReadOnlyList<ChartZoneDto> ResistanceZones);
