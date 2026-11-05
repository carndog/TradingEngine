using System.Text.Json.Serialization;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class EToroSearchItem
{
    [JsonPropertyName("instrumentId")]
    public long? InstrumentId { get; set; }

    [JsonPropertyName("internalSymbolFull")]
    public string? InternalSymbolFull { get; set; }

    [JsonPropertyName("internalExchangeId")]
    public int? InternalExchangeId { get; set; }

    [JsonPropertyName("internalExchangeName")]
    public string? InternalExchangeName { get; set; }

    [JsonPropertyName("exchangeID")]
    public int? ExchangeId { get; set; }
}
