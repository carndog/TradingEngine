namespace TradingEngine.Infrastructure.MarketData;

public static class EToroRateLimitPolicies
{
    public static readonly ProviderRateLimitPolicy MarketData = new(
        "etoro-market-data",
        120,
        TimeSpan.FromSeconds(60));

    public static readonly IReadOnlyList<string> MarketDataSharedEndpoints =
    [
        "GET /api/v1/market-data/exchanges",
        "GET /api/v1/market-data/instrument-types",
        "GET /api/v1/market-data/instruments",
        "GET /api/v1/market-data/instruments/history/closing-price",
        "GET /api/v1/market-data/instruments/rates",
        "GET /api/v1/market-data/instruments/{instrumentId}/history/candles/{direction}/{interval}/{candlesCount}",
        "GET /api/v1/market-data/search",
        "GET /api/v1/market-data/stocks-industries",
        "GET /api/v2/market-data/instruments",
        "GET /api/v2/market-data/instruments/search",
        "GET /api/v2/market-data/rates"
    ];

    public static int MinimumSamplingIntervalSeconds(int instrumentCount)
    {
        if (instrumentCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instrumentCount),
                instrumentCount,
                "The instrument count must be at least 1.");
        }

        double requestsPerSecond = MarketData.Limit / MarketData.Window.TotalSeconds;
        return (int)Math.Ceiling(instrumentCount / requestsPerSecond);
    }
}
