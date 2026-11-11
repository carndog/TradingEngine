using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.Tests.Notifications;

internal sealed class InMemoryNotificationIntentStore : INotificationIntentStore
{
    private readonly object _gate = new();

    public List<PendingNotification> Pending { get; } = [];

    public List<DispatchOutcome> Recorded { get; } = [];

    public HashSet<Guid> Unclaimable { get; } = [];

    public HashSet<Guid> Unrecordable { get; } = [];

    public Task<IReadOnlyList<PendingNotification>> ListDispatchableAsync(
        int maxCount,
        Instant now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<PendingNotification>>(
                Pending
                    .Where(item =>
                        item.Status is NotificationStatus.Pending
                            or NotificationStatus.Ambiguous)
                    .Take(maxCount)
                    .ToList());
        }
    }

    public Task<bool> TryClaimAsync(
        Guid intentId,
        Guid claimToken,
        Instant claimedUntil,
        Instant now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(Unclaimable.Contains(intentId) is false);
        }
    }

    public Task<bool> RecordOutcomeAsync(
        DispatchOutcome outcome,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Unrecordable.Contains(outcome.IntentId))
            {
                return Task.FromResult(false);
            }

            Recorded.Add(outcome);

            int index = Pending.FindIndex(
                item => item.IntentId == outcome.IntentId);
            if (index >= 0)
            {
                Pending[index] = Pending[index] with
                {
                    Status = outcome.Status,
                    AttemptCount = Pending[index].AttemptCount + 1
                };
            }

            return Task.FromResult(true);
        }
    }
}
