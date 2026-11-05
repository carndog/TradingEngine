namespace TradingEngine.Application.PriceCollection;

public enum PriceCollectionOutcome
{
    Observed = 1,
    SkippedMarketClosed = 2,
    SkippedDuplicate = 3,
    Throttled = 4,
    Failed = 5
}
