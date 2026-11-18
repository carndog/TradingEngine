using NodaTime;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.Notifications;

public sealed class NotificationDeliveryStoreAdapter(INotificationIntentStore store) : IEmailDeliveryStore
{
    public Task<NotificationClaim?> TryClaimAsync(Guid deliveryId, Guid claimToken, Instant claimedUntil,
        Instant now, CancellationToken cancellationToken)
    {
        return store.TryClaimAsync(deliveryId, claimToken, claimedUntil, now, cancellationToken);
    }

    public Task<BeginSubmissionResult> TryBeginSubmissionAsync(Guid deliveryId, Guid claimToken,
        string providerOperationId, int maxAttempts, Instant now, CancellationToken cancellationToken)
    {
        return store.TryBeginSubmissionAsync(deliveryId, claimToken, providerOperationId, maxAttempts, now, cancellationToken);
    }

    public Task<bool> RecordOutcomeAsync(EmailDeliveryOutcome outcome, CancellationToken cancellationToken)
    {
        return store.RecordOutcomeAsync(new DispatchOutcome(outcome.DeliveryId, outcome.ClaimToken,
            outcome.Status, outcome.AttemptOutcome, outcome.AttemptedAt, outcome.RecordedAt,
            outcome.ProviderOperationId, outcome.ProviderStatus, outcome.ErrorKind), cancellationToken);
    }
}
