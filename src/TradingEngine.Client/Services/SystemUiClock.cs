namespace TradingEngine.Client.Services;

public sealed class SystemUiClock : IUiClock
{
    public static SystemUiClock Instance { get; } = new();

    private SystemUiClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
