using NodaTime;
using TradingEngine.Application.Notifications;

namespace TradingEngine.Application.Ports;

public interface INotificationIntentStore
{
    Task<IReadOnlyList<PendingNotification>> ListDispatchableAsync(
        int maxCount,
        Instant now,
        CancellationToken cancellationToken);

    Task<NotificationClaim?> TryClaimAsync(
        Guid intentId,
        Guid claimToken,
        Instant claimedUntil,
        Instant now,
        CancellationToken cancellationToken);

    Task<BeginSubmissionResult> TryBeginSubmissionAsync(
        Guid intentId,
        Guid claimToken,
        string providerOperationId,
        int maxAttempts,
        Instant now,
        CancellationToken cancellationToken);

    Task<bool> RecordOutcomeAsync(
        DispatchOutcome outcome,
        CancellationToken cancellationToken);
}
