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

    public int MaxSearchPages { get; set; } = 5;

    public const int MinSearchPageSize = 1;

    public const int MaxSearchPageSize = 100;

    public int SearchPageSize { get; set; } = MaxSearchPageSize;

    public Dictionary<string, string[]> ExchangeMap { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, Dictionary<string, string>> QuoteCurrencyMap { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void NormalizeMappings()
    {
        ExchangeMap = new Dictionary<string, string[]>(
            ExchangeMap,
            StringComparer.OrdinalIgnoreCase);
        QuoteCurrencyMap = new Dictionary<string, Dictionary<string, string>>(
            QuoteCurrencyMap.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, string>(
                    pair.Value,
                    StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
    }
}
