namespace TradingEngine.Application.Tests.PriceCollection;

internal sealed class ManualTimeProvider : TimeProvider
{
    private sealed class PendingTimer : ITimer
    {
        public required TimerCallback Callback { get; init; }

        public object? State { get; init; }

        public TimeSpan DueTime { get; init; }

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

    private readonly object _gate = new();
    private readonly List<PendingTimer> _pending = [];
    private TimeSpan _now;

    public void Advance(TimeSpan by)
    {
        List<PendingTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _pending.Where(timer => timer.DueTime <= _now).ToList();
            foreach (PendingTimer timer in due)
            {
                _pending.Remove(timer);
            }
        }

        foreach (PendingTimer timer in due)
        {
            timer.Callback(timer.State);
        }
    }

    public int PendingTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        PendingTimer timer = new()
        {
            Callback = callback,
            State = state,
            DueTime = _now + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime)
        };
        lock (_gate)
        {
            _pending.Add(timer);
        }

        return timer;
    }
}
