using NodaTime;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.Notifications;

public sealed class EmailDeliveryDispatcher
{
    private readonly IEmailDeliveryStore _store;
    private readonly IEmailSender _sender;
    private readonly IClock _clock;
    private readonly int _claimLeaseSeconds;
    private readonly int _maxAttempts;

    public EmailDeliveryDispatcher(IEmailDeliveryStore store, IEmailSender sender, IClock clock,
        int claimLeaseSeconds, int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(claimLeaseSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _claimLeaseSeconds = claimLeaseSeconds;
        _maxAttempts = maxAttempts;
    }

    public async Task<EmailDispatchResult> DispatchAsync(
        EmailDeliveryWork delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();
        Guid claimToken = Guid.CreateVersion7();
        Instant claimedAt = _clock.GetCurrentInstant();
        Instant claimedUntil = claimedAt.Plus(
            Duration.FromSeconds(_claimLeaseSeconds));

        NotificationClaim? claim = await _store.TryClaimAsync(
            delivery.Id,
            claimToken,
            claimedUntil,
            claimedAt,
            cancellationToken);
        if (claim is null)
        {
            return Item(delivery, DispatchItemOutcome.ClaimSkipped, null);
        }

        bool submissionPersisted = claim.Status != NotificationStatus.Pending
            || claim.ProviderOperationId is not null;
        if (submissionPersisted)
        {
            return await ProbeSubmissionAsync(
                delivery,
                claim,
                claimToken,
                cancellationToken);
        }

        return await SubmitAsync(delivery, claimToken, cancellationToken);
    }

    private async Task<EmailDispatchResult> ProbeSubmissionAsync(
        EmailDeliveryWork delivery,
        NotificationClaim claim,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        string operationId = claim.ProviderOperationId
            ?? delivery.Id.ToString("D");

        EmailOperationProbe probe = await _sender.GetOperationStatusAsync(
            operationId,
            cancellationToken);

        bool acknowledged = claim.Status == NotificationStatus.Submitted;

        switch (probe.Status)
        {
            case EmailOperationStatus.Succeeded:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    NotificationStatus.Sent,
                    NotificationAttemptOutcome.ResolvedSucceeded,
                    null,
                    operationId,
                    probe.ProviderStatus,
                    DispatchItemOutcome.Sent,
                    cancellationToken);

            case EmailOperationStatus.Rejected:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    NotificationStatus.Failed,
                    NotificationAttemptOutcome.ResolvedRejected,
                    probe.ErrorKind,
                    operationId,
                    probe.ProviderStatus,
                    DispatchItemOutcome.Failed,
                    cancellationToken);

            case EmailOperationStatus.Unknown:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    acknowledged
                        ? NotificationStatus.Submitted
                        : NotificationStatus.Ambiguous,
                    NotificationAttemptOutcome.Unresolved,
                    probe.ErrorKind ?? "operation_unknown",
                    operationId,
                    probe.ProviderStatus,
                    DispatchItemOutcome.Ambiguous,
                    cancellationToken);

            case EmailOperationStatus.InFlight:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    NotificationStatus.Submitted,
                    NotificationAttemptOutcome.Unresolved,
                    null,
                    null,
                    probe.ProviderStatus,
                    DispatchItemOutcome.InFlight,
                    cancellationToken);

            default:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    acknowledged
                        ? NotificationStatus.Submitted
                        : NotificationStatus.Ambiguous,
                    NotificationAttemptOutcome.Unresolved,
                    probe.ErrorKind ?? probe.ProviderStatus ?? "in_flight",
                    null,
                    probe.ProviderStatus,
                    DispatchItemOutcome.Ambiguous,
                    cancellationToken);
        }
    }

    private async Task<EmailDispatchResult> SubmitAsync(
        EmailDeliveryWork delivery,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        BeginSubmissionResult began = await _store.TryBeginSubmissionAsync(
            delivery.Id,
            claimToken,
            delivery.Id.ToString("D"),
            _maxAttempts,
            _clock.GetCurrentInstant(),
            cancellationToken);

        switch (began)
        {
            case BeginSubmissionResult.Began:
                return await SendAsync(delivery, claimToken, cancellationToken);

            case BeginSubmissionResult.AttemptsExhausted:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    NotificationStatus.Failed,
                    NotificationAttemptOutcome.Suppressed,
                    "max_attempts_exceeded",
                    null,
                    null,
                    DispatchItemOutcome.Suppressed,
                    cancellationToken);

            default:
                return Item(delivery, DispatchItemOutcome.ClaimLost, null);
        }
    }

    private async Task<EmailDispatchResult> SendAsync(
        EmailDeliveryWork delivery,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        RecommendationEmail email = delivery.Render();

        EmailSendResult send;
        try
        {
            send = await _sender.SendAsync(
                email,
                delivery.Id,
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested is false)
        {
            send = new EmailSendResult(
                EmailSendOutcome.Ambiguous,
                null,
                "timeout");
        }

        string operationId = send.ProviderOperationId
            ?? delivery.Id.ToString("D");

        switch (send.Outcome)
        {
            case EmailSendOutcome.Accepted:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    NotificationStatus.Submitted,
                    NotificationAttemptOutcome.Submitted,
                    null,
                    operationId,
                    null,
                    DispatchItemOutcome.Submitted,
                    cancellationToken);

            case EmailSendOutcome.TransientFailure:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    NotificationStatus.Pending,
                    NotificationAttemptOutcome.Failed,
                    send.ErrorKind,
                    null,
                    null,
                    DispatchItemOutcome.RetryPending,
                    cancellationToken);

            case EmailSendOutcome.TerminalFailure:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    NotificationStatus.Failed,
                    NotificationAttemptOutcome.Failed,
                    send.ErrorKind,
                    null,
                    null,
                    DispatchItemOutcome.Failed,
                    cancellationToken);

            default:
                return await RecordAndMapAsync(
                    delivery,
                    claimToken,
                    NotificationStatus.Ambiguous,
                    NotificationAttemptOutcome.Ambiguous,
                    send.ErrorKind,
                    operationId,
                    null,
                    DispatchItemOutcome.Ambiguous,
                    cancellationToken);
        }
    }

    private async Task<EmailDispatchResult> RecordAndMapAsync(
        EmailDeliveryWork delivery,
        Guid claimToken,
        NotificationStatus status,
        NotificationAttemptOutcome attemptOutcome,
        string? errorKind,
        string? providerOperationId,
        string? providerStatus,
        DispatchItemOutcome mapped,
        CancellationToken cancellationToken)
    {
        Instant attemptedAt = _clock.GetCurrentInstant();

        bool recorded = await _store.RecordOutcomeAsync(
            new EmailDeliveryOutcome(
                delivery.Id,
                claimToken,
                status,
                attemptOutcome,
                attemptedAt,
                attemptedAt,
                providerOperationId,
                providerStatus,
                errorKind),
            cancellationToken);

        return recorded
            ? Item(delivery, mapped, errorKind)
            : Item(delivery, DispatchItemOutcome.ClaimLost, errorKind);
    }

    private static EmailDispatchResult Item(EmailDeliveryWork delivery, DispatchItemOutcome outcome, string? errorKind)
    {
        return new EmailDispatchResult(delivery.Id, outcome, errorKind);
    }
}
