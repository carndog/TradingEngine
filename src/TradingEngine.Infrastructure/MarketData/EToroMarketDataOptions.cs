using System.Text.Json;

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

    public string? QuoteCurrencyMapJson { get; set; }

    public void NormalizeMappings()
    {
        ExchangeMap = new Dictionary<string, string[]>(
            ExchangeMap,
            StringComparer.OrdinalIgnoreCase);
        QuoteCurrencyMap = MergeQuoteCurrencyMap();
    }

    private Dictionary<string, Dictionary<string, string>> MergeQuoteCurrencyMap()
    {
        Dictionary<string, Dictionary<string, string>> merged =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, Dictionary<string, string>> exchange
            in ParseQuoteCurrencyMapJson())
        {
            merged[exchange.Key] = new Dictionary<string, string>(
                exchange.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        foreach (KeyValuePair<string, Dictionary<string, string>> exchange
            in QuoteCurrencyMap)
        {
            if (merged.TryGetValue(
                    exchange.Key,
                    out Dictionary<string, string>? symbols) is false)
            {
                symbols = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                merged[exchange.Key] = symbols;
            }

            foreach (KeyValuePair<string, string> symbol in exchange.Value)
            {
                symbols[symbol.Key] = symbol.Value;
            }
        }

        return merged;
    }

    private IReadOnlyDictionary<string, Dictionary<string, string>>
        ParseQuoteCurrencyMapJson()
    {
        if (string.IsNullOrWhiteSpace(QuoteCurrencyMapJson))
        {
            return new Dictionary<string, Dictionary<string, string>>();
        }

        try
        {
            return JsonSerializer.Deserialize<
                    Dictionary<string, Dictionary<string, string>>>(
                    QuoteCurrencyMapJson)
                ?? new Dictionary<string, Dictionary<string, string>>();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{SectionName}:QuoteCurrencyMapJson must be a JSON object of " +
                "exchange-to-symbol native-currency entries.",
                exception);
        }
    }
}
