namespace TradingEngine.Infrastructure.MarketData;

public sealed class EToroMarketDataOptions
{
    public const string SectionName = "EToro:MarketData";

    public string BaseUrl { get; set; } = "https://public-api.etoro.com";

    public string? ApiKey { get; set; }

    public string? UserKey { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 5;

    public int MaxExecutionTimeSeconds { get; set; } = 15;

    public int MaxRetryAttempts { get; set; } = 2;

    public int ResolutionCacheSeconds { get; set; } = 86400;

    public Dictionary<string, string[]> ExchangeMap { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
