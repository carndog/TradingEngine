using System.Text.Json.Serialization;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class EToroSearchResponse
{
    [JsonPropertyName("totalItems")]
    public int TotalItems { get; set; }

    [JsonPropertyName("items")]
    public List<EToroSearchItem>? Items { get; set; }
}
