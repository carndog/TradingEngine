namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record WatchedInstrumentSummaryDto(
    Guid Id,
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    string MonitoringState,
    int SamplingIntervalSeconds);
