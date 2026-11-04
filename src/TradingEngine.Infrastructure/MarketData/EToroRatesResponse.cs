using System.Text.Json.Serialization;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class EToroRatesResponse
{
    [JsonPropertyName("results")]
    public List<EToroRate>? Results { get; set; }
}
