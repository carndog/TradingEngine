namespace TradingEngine.Application.PriceCollection;

public sealed class ExchangeSessionOptions
{
    public string? TimeZone { get; set; }

    public bool AlwaysOpen { get; set; }

    public List<SessionWindowOptions> Windows { get; set; } = [];
}
