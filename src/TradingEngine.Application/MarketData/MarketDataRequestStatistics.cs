namespace TradingEngine.Application.MarketData;

public sealed record MarketDataRequestStatistics(
    long SearchRequests,
    IReadOnlyDictionary<int, long> RateBatches)
{
    public static MarketDataRequestStatistics Empty { get; } = new(0, new Dictionary<int, long>());

    public long RatesRequests => RateBatches.Values.Sum();

    public MarketDataRequestStatistics Since(MarketDataRequestStatistics previous)
    {
        Dictionary<int, long> batches = RateBatches.ToDictionary(
            entry => entry.Key,
            entry => entry.Value - previous.RateBatches.GetValueOrDefault(entry.Key));
        return new MarketDataRequestStatistics(SearchRequests - previous.SearchRequests, batches);
    }
}
