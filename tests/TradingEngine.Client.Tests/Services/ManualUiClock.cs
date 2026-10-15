namespace TradingEngine.Client.Tests.Services;

internal sealed class ManualUiClock : IUiClock
{
    public ManualUiClock(DateTimeOffset now)
    {
        UtcNow = now;
    }

    public DateTimeOffset UtcNow { get; set; }

    public void Advance(TimeSpan by)
    {
        UtcNow += by;
    }
}
