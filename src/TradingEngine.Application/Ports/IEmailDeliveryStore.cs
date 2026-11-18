using NodaTime;
using TradingEngine.Application.Notifications;

namespace TradingEngine.Application.Ports;

public interface IEmailDeliveryStore
{
    Task<NotificationClaim?> TryClaimAsync(Guid deliveryId, Guid claimToken, Instant claimedUntil,
        Instant now, CancellationToken cancellationToken);

    Task<BeginSubmissionResult> TryBeginSubmissionAsync(Guid deliveryId, Guid claimToken,
        string providerOperationId, int maxAttempts, Instant now, CancellationToken cancellationToken);

    Task<bool> RecordOutcomeAsync(EmailDeliveryOutcome outcome, CancellationToken cancellationToken);
}
