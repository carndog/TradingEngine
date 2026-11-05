namespace TradingEngine.Infrastructure.Tests.MarketData;

internal sealed class InstantTimer : ITimer
{
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        return false;
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
