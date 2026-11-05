using System.Text.Json.Serialization;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class EToroGatewayError
{
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}
