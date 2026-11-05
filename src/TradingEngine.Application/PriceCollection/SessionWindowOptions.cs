namespace TradingEngine.Application.PriceCollection;

public sealed class SessionWindowOptions
{
    public List<string> Days { get; set; } = [];

    public string? Open { get; set; }

    public string? Close { get; set; }
}
