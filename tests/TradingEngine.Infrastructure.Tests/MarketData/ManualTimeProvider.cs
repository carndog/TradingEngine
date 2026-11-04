namespace TradingEngine.Infrastructure.Tests.MarketData;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Action<TimeSpan> _onDelay;
    private readonly bool _autoFire;
    private readonly Queue<(TimerCallback Callback, object? State)> _pending = new();

    public ManualTimeProvider(Action<TimeSpan>? onDelay = null, bool autoFire = true)
    {
        _onDelay = onDelay ?? (_ => { });
        _autoFire = autoFire;
    }

    public List<TimeSpan> DelayRequests { get; } = [];

    public int PendingTimerCount => _pending.Count;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        DelayRequests.Add(dueTime);
        _onDelay(dueTime);
        if (_autoFire)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
        else
        {
            _pending.Enqueue((callback, state));
        }

        return new InstantTimer();
    }

    public void FireAll()
    {
        while (_pending.Count > 0)
        {
            (TimerCallback callback, object? state) = _pending.Dequeue();
            callback(state);
        }
    }
}
