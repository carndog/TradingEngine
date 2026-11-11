using NodaTime;
using TradingEngine.Application.Notifications;

namespace TradingEngine.Application.Ports;

public interface INotificationIntentStore
{
    Task<IReadOnlyList<PendingNotification>> ListDispatchableAsync(
        int maxCount,
        Instant now,
        CancellationToken cancellationToken);

    Task<bool> TryClaimAsync(
        Guid intentId,
        Guid claimToken,
        Instant claimedUntil,
        Instant now,
        CancellationToken cancellationToken);

    Task<bool> RecordOutcomeAsync(
        DispatchOutcome outcome,
        CancellationToken cancellationToken);
}
