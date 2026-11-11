using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.Notifications;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.Tests.Notifications;

[TestFixture]
public sealed class DispatchPendingNotificationsHandlerTests
{
    private static readonly Guid InstrumentId =
        Guid.Parse("3f8a2c1d-9e4b-4a5c-b7d6-2e1f0a9b8c7d");
    private static readonly Guid RevisionId =
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 30, 13, 45, 30);
    private static readonly Instant Slot = Instant.FromUtc(2026, 10, 30, 13, 44, 0);

    private FakeClock _clock = null!;
    private InMemoryNotificationIntentStore _store = null!;
    private RecordingEmailSender _sender = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _store = new InMemoryNotificationIntentStore();
        _sender = new RecordingEmailSender();
    }

    [Test]
    public async Task HandleAsync_WhenSendAccepted_MarksSubmittedPendingProbe()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Submitted));
            Assert.That(_sender.Sends, Has.Count.EqualTo(1));
            Assert.That(_sender.Sends[0].OperationId, Is.EqualTo(intent.IntentId));
            Assert.That(_store.Recorded, Has.Count.EqualTo(1));

            DispatchOutcome recorded = _store.Recorded[0];
            Assert.That(recorded.Status, Is.EqualTo(NotificationStatus.Submitted));
            Assert.That(
                recorded.AttemptOutcome,
                Is.EqualTo(NotificationAttemptOutcome.Submitted));
            Assert.That(
                recorded.ProviderOperationId,
                Is.EqualTo(intent.IntentId.ToString("D")));
            Assert.That(recorded.AttemptedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSendAmbiguousThenProbeSucceeds_ResolvesWithoutResend()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);
        _sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.Ambiguous, null, "timeout"));
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));

        DispatchPendingNotificationsHandler handler = CreateHandler();
        DispatchRunResult first = await handler.HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);
        DispatchRunResult second = await handler.HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                first.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Ambiguous));
            Assert.That(
                _store.Recorded[0].Status,
                Is.EqualTo(NotificationStatus.Ambiguous));
            Assert.That(
                _store.Recorded[0].ProviderOperationId,
                Is.EqualTo(intent.IntentId.ToString("D")));

            Assert.That(
                second.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(_sender.Sends, Has.Count.EqualTo(1));
            Assert.That(_sender.Probes, Has.Count.EqualTo(1));
            Assert.That(
                _sender.Probes[0],
                Is.EqualTo(intent.IntentId.ToString("D")));
            Assert.That(
                _store.Recorded[1].Status,
                Is.EqualTo(NotificationStatus.Sent));
            Assert.That(
                _store.Recorded[1].AttemptOutcome,
                Is.EqualTo(NotificationAttemptOutcome.ResolvedSucceeded));
        });
    }

    [Test]
    public async Task HandleAsync_WhenProbeUnknown_ResendsWithSameOperationId()
    {
        PendingNotification intent = Pending(NotificationStatus.Ambiguous);
        _store.Pending.Add(intent);
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Unknown, null, null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Submitted));
            Assert.That(_sender.Sends, Has.Count.EqualTo(1));
            Assert.That(_sender.Sends[0].OperationId, Is.EqualTo(intent.IntentId));
        });
    }

    [Test]
    public async Task HandleAsync_WhenProbeStillInFlight_ReportsInFlightWithoutFailure()
    {
        PendingNotification intent = Pending(NotificationStatus.Ambiguous);
        _store.Pending.Add(intent);
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.InFlight, "Running", null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.InFlight));
            Assert.That(_sender.Sends, Is.Empty);
            Assert.That(
                _store.Recorded[0].Status,
                Is.EqualTo(NotificationStatus.Ambiguous));
            Assert.That(
                _store.Recorded[0].AttemptOutcome,
                Is.EqualTo(NotificationAttemptOutcome.Unresolved));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSubmittedProbeRunning_ReportsInFlightKeepsAcknowledged()
    {
        PendingNotification intent = Pending(NotificationStatus.Submitted);
        _store.Pending.Add(intent);
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.InFlight, "Running", null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.InFlight));
            Assert.That(_sender.Sends, Is.Empty);
            Assert.That(
                _store.Recorded[0].Status,
                Is.EqualTo(NotificationStatus.Submitted));
            Assert.That(
                _store.Recorded[0].AttemptOutcome,
                Is.EqualTo(NotificationAttemptOutcome.Unresolved));
        });
    }

    [Test]
    public async Task HandleAsync_WhenAcknowledgedProbeNotFound_StaysSubmittedWithoutResend()
    {
        PendingNotification intent = Pending(NotificationStatus.Submitted);
        _store.Pending.Add(intent);
        _store.ClaimOverrides[intent.IntentId] = new NotificationClaim(
            NotificationStatus.Submitted,
            intent.IntentId.ToString("D"));
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Unknown, null, null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Ambiguous));
            Assert.That(_sender.Sends, Is.Empty);
            Assert.That(
                _store.Recorded[0].Status,
                Is.EqualTo(NotificationStatus.Submitted));
            Assert.That(
                _store.Recorded[0].AttemptOutcome,
                Is.EqualTo(NotificationAttemptOutcome.Unresolved));
        });
    }

    [Test]
    public async Task HandleAsync_WhenAcceptedThenNotFoundThenRecovered_SendsOnce()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);
        _sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.Accepted, null, null));
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Unknown, null, null));
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));

        DispatchPendingNotificationsHandler handler = CreateHandler();
        await handler.HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);
        await handler.HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);
        DispatchRunResult third = await handler.HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_sender.Sends, Has.Count.EqualTo(1));
            Assert.That(
                third.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(
                _store.Recorded[^1].Status,
                Is.EqualTo(NotificationStatus.Sent));
        });
    }

    [Test]
    public async Task HandleAsync_WhenUnacknowledgedProbeNotFound_ResendsWithSameOperationId()
    {
        PendingNotification intent = Pending(NotificationStatus.Ambiguous);
        _store.Pending.Add(intent);
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Unknown, null, null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_sender.Sends, Has.Count.EqualTo(1));
            Assert.That(_sender.Sends[0].OperationId, Is.EqualTo(intent.IntentId));
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Submitted));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSendTransientlyFails_StaysPendingForRetry()
    {
        _store.Pending.Add(Pending());
        _sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.TransientFailure, null, "http_429"));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.RetryPending));
            Assert.That(
                _store.Recorded[0].Status,
                Is.EqualTo(NotificationStatus.Pending));
            Assert.That(
                _store.Recorded[0].AttemptOutcome,
                Is.EqualTo(NotificationAttemptOutcome.Failed));
            Assert.That(_store.Recorded[0].ErrorKind, Is.EqualTo("http_429"));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSendTerminallyFails_MarksFailed()
    {
        _store.Pending.Add(Pending());
        _sender.SendResults.Enqueue(
            new EmailSendResult(EmailSendOutcome.TerminalFailure, null, "http_400"));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Outcome, Is.EqualTo(DispatchItemOutcome.Failed));
            Assert.That(
                _store.Recorded[0].Status,
                Is.EqualTo(NotificationStatus.Failed));
        });
    }

    [Test]
    public async Task HandleAsync_WhenAttemptsExhausted_SuppressesWithoutSending()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);
        SeedSendAttempts(intent.IntentId, 3);

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Suppressed));
            Assert.That(_sender.Sends, Is.Empty);
            Assert.That(
                _store.Recorded[^1].Status,
                Is.EqualTo(NotificationStatus.Failed));
            Assert.That(
                _store.Recorded[^1].AttemptOutcome,
                Is.EqualTo(NotificationAttemptOutcome.Suppressed));
        });
    }

    [Test]
    public async Task HandleAsync_WhenClaimDenied_SkipsWithoutSending()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);
        _store.Unclaimable.Add(intent.IntentId);

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.ClaimSkipped));
            Assert.That(_sender.Sends, Is.Empty);
            Assert.That(_store.Recorded, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenOutcomeRecordRejected_ReportsClaimLost()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);
        _store.Unrecordable.Add(intent.IntentId);

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.ClaimLost));
            Assert.That(_sender.Sends, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenSendTimesOutWithoutCallerCancellation_TreatedAsAmbiguous()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);
        _sender.SendHook = (_, _) =>
            throw new OperationCanceledException("simulated client timeout");

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Ambiguous));
            Assert.That(
                _store.Recorded[0].Status,
                Is.EqualTo(NotificationStatus.Ambiguous));
        });
    }

    [Test]
    public async Task HandleAsync_WhenPendingWithPersistedOperation_ProbesBeforeResend()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);
        string operationId = intent.IntentId.ToString("D");
        _store.ClaimOverrides[intent.IntentId] = new NotificationClaim(
            NotificationStatus.Pending,
            operationId);
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Outcome, Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(_sender.Sends, Is.Empty);
            Assert.That(_sender.Probes, Has.Count.EqualTo(1));
            Assert.That(_sender.Probes[0], Is.EqualTo(operationId));
            Assert.That(
                _store.Recorded[0].Status,
                Is.EqualTo(NotificationStatus.Sent));
        });
    }

    [Test]
    public async Task HandleAsync_WhenClaimReturnsAmbiguousOnStaleCandidate_ProbesNotSends()
    {
        PendingNotification intent = Pending();
        _store.Pending.Add(intent);
        _store.ClaimOverrides[intent.IntentId] = new NotificationClaim(
            NotificationStatus.Ambiguous,
            intent.IntentId.ToString("D"));
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Items[0].Outcome, Is.EqualTo(DispatchItemOutcome.Sent));
            Assert.That(_sender.Sends, Is.Empty);
            Assert.That(_sender.Probes, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task HandleAsync_WhenOperationRunningAfterAttemptLimit_StaysAmbiguousWithoutSend()
    {
        PendingNotification intent = Pending(
            NotificationStatus.Ambiguous,
            attemptCount: 3);
        _store.Pending.Add(intent);
        SeedSendAttempts(intent.IntentId, 3);
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.InFlight, "Running", null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.InFlight));
            Assert.That(_sender.Sends, Is.Empty);
            Assert.That(
                _store.Recorded[^1].Status,
                Is.EqualTo(NotificationStatus.Ambiguous));
            Assert.That(
                _store.Recorded[^1].AttemptOutcome,
                Is.EqualTo(NotificationAttemptOutcome.Unresolved));
            Assert.That(
                _store.Pending[0].Status,
                Is.EqualTo(NotificationStatus.Ambiguous));
        });
    }

    [Test]
    public async Task HandleAsync_WhenAttemptsExhaustedUnderClaim_SuppressesWithoutSending()
    {
        PendingNotification intent = Pending(attemptCount: 1);
        _store.Pending.Add(intent);
        SeedSendAttempts(intent.IntentId, 3);

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].Outcome,
                Is.EqualTo(DispatchItemOutcome.Suppressed));
            Assert.That(_sender.Sends, Is.Empty);
        });
    }

    [Test]
    public async Task HandleAsync_WhenFreshPendingCompetesWithUnresolved_SubmitsNewIntentFirst()
    {
        PendingNotification unresolved = Pending(
            NotificationStatus.Submitted,
            attemptCount: 1);
        PendingNotification fresh = Pending();
        _store.Pending.Add(unresolved);
        _store.Pending.Add(fresh);
        _sender.ProbeResults.Enqueue(
            new EmailOperationProbe(EmailOperationStatus.InFlight, "Running", null));

        DispatchRunResult result = await CreateHandler().HandleAsync(
            new DispatchPendingNotifications(),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.Items[0].IntentId,
                Is.EqualTo(fresh.IntentId));
            Assert.That(_sender.Sends, Has.Count.EqualTo(1));
            Assert.That(_sender.Sends[0].OperationId, Is.EqualTo(fresh.IntentId));
        });
    }

    private void SeedSendAttempts(Guid intentId, int count)
    {
        for (int index = 0; index < count; index++)
        {
            _store.Recorded.Add(new DispatchOutcome(
                intentId,
                Guid.NewGuid(),
                NotificationStatus.Submitted,
                NotificationAttemptOutcome.Submitted,
                Now,
                Now,
                intentId.ToString("D"),
                "Started",
                null));
        }
    }

    private DispatchPendingNotificationsHandler CreateHandler()
    {
        return new DispatchPendingNotificationsHandler(
            _store,
            _sender,
            new RecommendationEmailRenderer(),
            _clock,
            new NotificationOptions { MaxDispatchAttempts = 3 });
    }

    private static PendingNotification Pending(
        NotificationStatus status = NotificationStatus.Pending,
        int attemptCount = 0)
    {
        return new PendingNotification(
            Guid.CreateVersion7(),
            InstrumentId,
            "DEMO",
            "XNAS",
            Slot,
            Slot.Minus(Duration.FromMinutes(1)),
            RevisionId,
            PriceBasis.Bid,
            "USD",
            100m,
            101m,
            [Match()],
            status,
            attemptCount,
            null);
    }

    private static TriggeredCondition Match()
    {
        return new TriggeredCondition(
            ChartAnalysisIdentifier.From("support-a").Value,
            ChartConditionType.BuyZone,
            ChartAnalysisIdentifier.From("notify").Value,
            95m,
            100m,
            105m,
            120m,
            100m);
    }
}
