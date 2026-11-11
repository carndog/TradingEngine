using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;

namespace TradingEngine.Application.Notifications;

public sealed class DispatchPendingNotificationsHandler
{
    private readonly INotificationIntentStore _store;
    private readonly IEmailSender _sender;
    private readonly RecommendationEmailRenderer _renderer;
    private readonly IClock _clock;
    private readonly NotificationOptions _options;

    public DispatchPendingNotificationsHandler(
        INotificationIntentStore store,
        IEmailSender sender,
        RecommendationEmailRenderer renderer,
        IClock clock,
        NotificationOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<DispatchRunResult> HandleAsync(
        DispatchPendingNotifications command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Instant startedAt = PersistedInstant.Require(
            _clock.GetCurrentInstant(),
            nameof(command));
        Guid invocationId = Guid.CreateVersion7();
        Instant now = _clock.GetCurrentInstant();

        IReadOnlyList<PendingNotification> intents =
            await _store.ListDispatchableAsync(
                _options.MaxIntentsPerRun,
                now,
                cancellationToken);

        List<IntentDispatchResult> items = [];
        foreach (PendingNotification intent in intents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            items.Add(await DispatchIntentAsync(intent, cancellationToken));
        }

        return new DispatchRunResult(
            invocationId,
            startedAt,
            _clock.GetCurrentInstant(),
            items);
    }

    private async Task<IntentDispatchResult> DispatchIntentAsync(
        PendingNotification intent,
        CancellationToken cancellationToken)
    {
        Guid claimToken = Guid.CreateVersion7();
        Instant claimedAt = _clock.GetCurrentInstant();
        Instant claimedUntil = claimedAt.Plus(
            Duration.FromSeconds(_options.ClaimLeaseSeconds));

        NotificationClaim? claim = await _store.TryClaimAsync(
            intent.IntentId,
            claimToken,
            claimedUntil,
            claimedAt,
            cancellationToken);
        if (claim is null)
        {
            return Item(intent, DispatchItemOutcome.ClaimSkipped, null);
        }

        bool submissionPersisted = claim.Status != NotificationStatus.Pending
            || claim.ProviderOperationId is not null;
        if (submissionPersisted)
        {
            return await ProbeSubmissionAsync(
                intent,
                claim,
                claimToken,
                cancellationToken);
        }

        return await SubmitAsync(intent, claimToken, cancellationToken);
    }

    private async Task<IntentDispatchResult> ProbeSubmissionAsync(
        PendingNotification intent,
        NotificationClaim claim,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        string operationId = claim.ProviderOperationId
            ?? intent.IntentId.ToString("D");

        EmailOperationProbe probe = await _sender.GetOperationStatusAsync(
            operationId,
            cancellationToken);

        bool acknowledged = claim.Status == NotificationStatus.Submitted;
        NotificationStatus unresolvedStatus = acknowledged
            ? NotificationStatus.Submitted
            : NotificationStatus.Ambiguous;

        switch (probe.Status)
        {
            case EmailOperationStatus.Succeeded:
                return await RecordAndMapAsync(
                    intent,
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
                    intent,
                    claimToken,
                    NotificationStatus.Failed,
                    NotificationAttemptOutcome.ResolvedRejected,
                    probe.ErrorKind,
                    operationId,
                    probe.ProviderStatus,
                    DispatchItemOutcome.Failed,
                    cancellationToken);

            case EmailOperationStatus.Unknown:
                if (acknowledged)
                {
                    return await RecordAndMapAsync(
                        intent,
                        claimToken,
                        NotificationStatus.Submitted,
                        NotificationAttemptOutcome.Unresolved,
                        probe.ErrorKind ?? "operation_unknown",
                        operationId,
                        probe.ProviderStatus,
                        DispatchItemOutcome.Ambiguous,
                        cancellationToken);
                }

                return await SubmitAsync(intent, claimToken, cancellationToken);

            case EmailOperationStatus.InFlight:
                return await RecordAndMapAsync(
                    intent,
                    claimToken,
                    unresolvedStatus,
                    NotificationAttemptOutcome.Unresolved,
                    null,
                    null,
                    probe.ProviderStatus,
                    DispatchItemOutcome.InFlight,
                    cancellationToken);

            default:
                return await RecordAndMapAsync(
                    intent,
                    claimToken,
                    unresolvedStatus,
                    NotificationAttemptOutcome.Unresolved,
                    probe.ErrorKind ?? probe.ProviderStatus ?? "in_flight",
                    null,
                    probe.ProviderStatus,
                    DispatchItemOutcome.Ambiguous,
                    cancellationToken);
        }
    }

    private async Task<IntentDispatchResult> SubmitAsync(
        PendingNotification intent,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        BeginSubmissionResult began = await _store.TryBeginSubmissionAsync(
            intent.IntentId,
            claimToken,
            intent.IntentId.ToString("D"),
            _options.MaxDispatchAttempts,
            _clock.GetCurrentInstant(),
            cancellationToken);

        switch (began)
        {
            case BeginSubmissionResult.Began:
                return await SendAsync(intent, claimToken, cancellationToken);

            case BeginSubmissionResult.AttemptsExhausted:
                return await RecordAndMapAsync(
                    intent,
                    claimToken,
                    NotificationStatus.Failed,
                    NotificationAttemptOutcome.Suppressed,
                    "max_attempts_exceeded",
                    null,
                    null,
                    DispatchItemOutcome.Suppressed,
                    cancellationToken);

            default:
                return Item(intent, DispatchItemOutcome.ClaimLost, null);
        }
    }

    private async Task<IntentDispatchResult> SendAsync(
        PendingNotification intent,
        Guid claimToken,
        CancellationToken cancellationToken)
    {
        RecommendationEmail email = _renderer.Render(intent);

        EmailSendResult send;
        try
        {
            send = await _sender.SendAsync(
                email,
                intent.IntentId,
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
            ?? intent.IntentId.ToString("D");

        switch (send.Outcome)
        {
            case EmailSendOutcome.Accepted:
                return await RecordAndMapAsync(
                    intent,
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
                    intent,
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
                    intent,
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
                    intent,
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

    private async Task<IntentDispatchResult> RecordAndMapAsync(
        PendingNotification intent,
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
            new DispatchOutcome(
                intent.IntentId,
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
            ? Item(intent, mapped, errorKind)
            : Item(intent, DispatchItemOutcome.ClaimLost, errorKind);
    }

    private static IntentDispatchResult Item(
        PendingNotification intent,
        DispatchItemOutcome outcome,
        string? errorKind)
    {
        return new IntentDispatchResult(
            intent.IntentId,
            intent.InstrumentId,
            intent.ScheduledFor,
            intent.MonitoringRuleRevisionId,
            intent.CollectionInvocationId,
            outcome,
            errorKind);
    }
}
