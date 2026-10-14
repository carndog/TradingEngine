namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record WatchedInstrumentSettingsResponse(
    Guid Id,
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    string MonitoringState,
    int SamplingIntervalSeconds,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastChangedAt,
    string ConcurrencyToken);
