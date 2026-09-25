namespace TradingEngine.Domain.Instruments;

public sealed record WatchedInstrumentFields(
    string Symbol,
    string Exchange,
    string QuoteCurrency,
    int SamplingIntervalSeconds);
