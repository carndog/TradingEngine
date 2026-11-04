namespace TradingEngine.Infrastructure.Tests.MarketData;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Action<TimeSpan> _onDelay;

    public ManualTimeProvider(Action<TimeSpan>? onDelay = null)
    {
        _onDelay = onDelay ?? (_ => { });
    }

    public List<TimeSpan> DelayRequests { get; } = [];

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        DelayRequests.Add(dueTime);
        _onDelay(dueTime);
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new InstantTimer();
    }
}
