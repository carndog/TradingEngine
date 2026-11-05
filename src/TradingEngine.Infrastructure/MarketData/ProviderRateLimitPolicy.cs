namespace TradingEngine.Infrastructure.MarketData;

public sealed record ProviderRateLimitPolicy(
    string Name,
    int Limit,
    TimeSpan Window);
