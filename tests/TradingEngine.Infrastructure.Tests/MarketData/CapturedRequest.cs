namespace TradingEngine.Infrastructure.Tests.MarketData;

internal sealed record CapturedRequest(
    Uri? RequestUri,
    IReadOnlyDictionary<string, string[]> Headers)
{
    public string? HeaderValue(string name)
    {
        return Headers.TryGetValue(name, out string[]? values)
            ? values.FirstOrDefault()
            : null;
    }
}
