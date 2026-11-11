namespace TradingEngine.Application.Ports;

public sealed class RecommendationStorageException : Exception
{
    public RecommendationStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
