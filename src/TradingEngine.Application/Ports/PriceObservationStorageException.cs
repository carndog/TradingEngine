namespace TradingEngine.Application.Ports;

public sealed class PriceObservationStorageException : Exception
{
    public PriceObservationStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
