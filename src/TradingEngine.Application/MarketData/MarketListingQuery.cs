namespace TradingEngine.Application.MarketData;

public sealed record MarketListingQuery(
    string Exchange,
    string Symbol);
