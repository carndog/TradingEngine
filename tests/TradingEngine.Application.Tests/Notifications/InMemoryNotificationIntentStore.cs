using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.Tests.Notifications;

internal sealed class InMemoryNotificationIntentStore : INotificationIntentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, string?> _providerOperations = [];

    public List<PendingNotification> Pending { get; } = [];

    public List<DispatchOutcome> Recorded { get; } = [];

    public HashSet<Guid> Unclaimable { get; } = [];

    public HashSet<Guid> Unrecordable { get; } = [];

    public HashSet<Guid> Unsubmittable { get; } = [];

    public Dictionary<Guid, NotificationClaim> ClaimOverrides { get; } = [];

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
                            or NotificationStatus.Ambiguous
                            or NotificationStatus.Submitted)
                    .OrderBy(item => item.AttemptCount > 0 ? 1 : 0)
                    .Take(maxCount)
                    .ToList());
        }
    }

    public Task<NotificationClaim?> TryClaimAsync(
        Guid intentId,
        Guid claimToken,
        Instant claimedUntil,
        Instant now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Unclaimable.Contains(intentId))
            {
                return Task.FromResult<NotificationClaim?>(null);
            }

            if (ClaimOverrides.TryGetValue(intentId, out NotificationClaim? overrideClaim))
            {
                return Task.FromResult<NotificationClaim?>(overrideClaim);
            }

            int index = Pending.FindIndex(item => item.IntentId == intentId);
            if (index < 0)
            {
                return Task.FromResult<NotificationClaim?>(null);
            }

            _providerOperations.TryGetValue(intentId, out string? operationId);
            return Task.FromResult<NotificationClaim?>(
                new NotificationClaim(Pending[index].Status, operationId));
        }
    }

    public Task<BeginSubmissionResult> TryBeginSubmissionAsync(
        Guid intentId,
        Guid claimToken,
        string providerOperationId,
        int maxAttempts,
        Instant now,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (Unsubmittable.Contains(intentId))
            {
                return Task.FromResult(BeginSubmissionResult.ClaimLost);
            }

            int sendAttempts = Recorded.Count(record =>
                record.IntentId == intentId
                && record.AttemptOutcome is NotificationAttemptOutcome.Submitted
                    or NotificationAttemptOutcome.Failed
                    or NotificationAttemptOutcome.Ambiguous);
            if (sendAttempts >= maxAttempts)
            {
                return Task.FromResult(BeginSubmissionResult.AttemptsExhausted);
            }

            _providerOperations[intentId] = providerOperationId;
            return Task.FromResult(BeginSubmissionResult.Began);
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
            if (outcome.AttemptOutcome == NotificationAttemptOutcome.Failed)
            {
                _providerOperations.Remove(outcome.IntentId);
            }
            else if (outcome.ProviderOperationId is not null)
            {
                _providerOperations[outcome.IntentId] = outcome.ProviderOperationId;
            }

            int index = Pending.FindIndex(
                item => item.IntentId == outcome.IntentId);
            if (index >= 0)
            {
                bool consumedSendBudget = outcome.AttemptOutcome is
                    NotificationAttemptOutcome.Submitted
                    or NotificationAttemptOutcome.Failed
                    or NotificationAttemptOutcome.Ambiguous;
                Pending[index] = Pending[index] with
                {
                    Status = outcome.Status,
                    AttemptCount = consumedSendBudget
                        ? Pending[index].AttemptCount + 1
                        : Pending[index].AttemptCount
                };
            }

            return Task.FromResult(true);
        }
    }
}
