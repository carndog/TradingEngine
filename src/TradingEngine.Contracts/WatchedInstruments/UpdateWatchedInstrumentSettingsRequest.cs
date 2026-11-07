namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record UpdateWatchedInstrumentSettingsRequest(
    string? MonitoringState,
    int SamplingIntervalSeconds,
    int? PriceDisplayDecimalPlaces = null,
    string? PriceDisplayUnit = null);
