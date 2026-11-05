namespace TradingEngine.Infrastructure.MarketData;

public sealed class EToroMarketDataException : Exception
{
    public EToroMarketDataException(string message)
        : base(message)
    {
    }

    public EToroMarketDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
