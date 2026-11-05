using System.Text.Json.Serialization;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class EToroRate
{
    [JsonPropertyName("instrumentId")]
    public long? InstrumentId { get; set; }

    [JsonPropertyName("bid")]
    public decimal? Bid { get; set; }

    [JsonPropertyName("ask")]
    public decimal? Ask { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("quoteType")]
    public string? QuoteType { get; set; }
}
