namespace TradingEngine.Application.MarketData;

public sealed record MarketInstrumentIdentity(
    string Exchange,
    string Symbol,
    string QuoteCurrency);
