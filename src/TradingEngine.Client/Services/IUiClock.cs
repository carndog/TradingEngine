namespace TradingEngine.Client.Services;

public interface IUiClock
{
    DateTimeOffset UtcNow { get; }
}
