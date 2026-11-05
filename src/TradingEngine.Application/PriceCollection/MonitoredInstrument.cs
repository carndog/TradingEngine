namespace TradingEngine.Application.PriceCollection;

public sealed record MonitoredInstrument(
    Guid Id,
    string Exchange,
    string Symbol,
    string QuoteCurrency,
    int SamplingIntervalSeconds);
