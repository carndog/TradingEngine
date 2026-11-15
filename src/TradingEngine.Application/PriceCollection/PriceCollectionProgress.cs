using NodaTime;

namespace TradingEngine.Application.PriceCollection;

public sealed class PriceCollectionProgress
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Instant> _unfinishedSlots = [];
    private Guid? _lastStarted;
    private Guid? _interruptedInstrument;

    public Guid? InterruptedInstrument
    {
        get
        {
            lock (_gate)
            {
                return _interruptedInstrument;
            }
        }
    }

    public void FinishRun(bool deadlineReached)
    {
        lock (_gate)
        {
            _interruptedInstrument = deadlineReached ? _lastStarted : null;
        }
    }

    public IReadOnlyList<MonitoredInstrument> Order(IReadOnlyList<MonitoredInstrument> instruments)
    {
        lock (_gate)
        {
            int index = instruments.ToList().FindIndex(instrument => instrument.Id == _lastStarted);
            return instruments.Skip(index + 1).Concat(instruments.Take(index + 1)).ToArray();
        }
    }

    public void AdvancePast(Guid instrumentId)
    {
        lock (_gate)
        {
            _lastStarted = instrumentId;
        }
    }

    public int TrackDueSlots(
        IReadOnlyDictionary<Guid, Instant> dueSlots,
        IReadOnlyDictionary<Guid, Instant> latestSlots,
        IReadOnlyCollection<Guid> monitoredIds)
    {
        lock (_gate)
        {
            int expired = 0;
            HashSet<Guid> monitored = monitoredIds.ToHashSet();
            foreach (KeyValuePair<Guid, Instant> pending in _unfinishedSlots.ToArray())
            {
                if (monitored.Contains(pending.Key) is false
                    || latestSlots.TryGetValue(pending.Key, out Instant saved) && saved >= pending.Value)
                {
                    _unfinishedSlots.Remove(pending.Key);
                }
                else if (dueSlots.TryGetValue(pending.Key, out Instant current) && current > pending.Value)
                {
                    expired++;
                    _unfinishedSlots.Remove(pending.Key);
                }
            }

            foreach (KeyValuePair<Guid, Instant> due in dueSlots)
            {
                _unfinishedSlots[due.Key] = due.Value;
            }

            return expired;
        }
    }

    public void Complete(Guid instrumentId)
    {
        lock (_gate)
        {
            _unfinishedSlots.Remove(instrumentId);
        }
    }
}
