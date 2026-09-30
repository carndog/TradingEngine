namespace TradingEngine.Api.Diagnostics;

internal sealed class TelemetrySanitizedException : Exception
{
    public TelemetrySanitizedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
