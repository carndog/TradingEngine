using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.Tests.Notifications;

[TestFixture]
public sealed class EmailDeliveryDispatcherTests : IEmailDeliveryStore
{
    private NotificationStatus _status;
    private string? _operation;
    private bool _loseOutcome;
    private bool _claimable;
    private int _attempts;
    private RecordingEmailSender _sender = null!;

    [SetUp]
    public void SetUp()
    {
        _status = NotificationStatus.Pending;
        _operation = null;
        _loseOutcome = false;
        _claimable = true;
        _attempts = 0;
        _sender = new RecordingEmailSender();
    }

    [Test]
    public async Task DispatchAsync_AcceptedSendWithLostOutcome_ReconcilesPersistedOperationWithoutResending()
    {
        Guid reportId = Guid.NewGuid();
        RecommendationEmail snapshot = new("synthetic weekly report", "immutable body", "<p>immutable body</p>", ["report@synthetic.example"]);
        int renders = 0;
        EmailDeliveryWork work = new(reportId, () => { renders++; return snapshot; });
        EmailDeliveryDispatcher dispatcher = Dispatcher();
        _loseOutcome = true;

        EmailDispatchResult first = await dispatcher.DispatchAsync(work, CancellationToken.None);
        _loseOutcome = false;
        _sender.ProbeResults.Enqueue(new EmailOperationProbe(EmailOperationStatus.Succeeded, "Succeeded", null));
        EmailDispatchResult recovered = await dispatcher.DispatchAsync(work, CancellationToken.None);

        Assert.That(first.Outcome, Is.EqualTo(DispatchItemOutcome.ClaimLost));
        Assert.That(recovered.Outcome, Is.EqualTo(DispatchItemOutcome.Sent));
        Assert.That(_sender.Sends, Has.Count.EqualTo(1));
        Assert.That(_sender.Probes.Single(), Is.EqualTo(reportId.ToString("D")));
        Assert.That(renders, Is.EqualTo(1));
        Assert.That(_sender.Sends.Single().Email, Is.SameAs(snapshot));
    }

    [Test]
    public async Task DispatchAsync_UnknownAcknowledgedOperation_RemainsSubmittedWithoutAnotherSend()
    {
        _status = NotificationStatus.Submitted;
        _operation = Guid.NewGuid().ToString("D");
        EmailDispatchResult result = await Dispatcher().DispatchAsync(new EmailDeliveryWork(Guid.NewGuid(),
            () => throw new InvalidOperationException("Probe must not render.")), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(DispatchItemOutcome.Ambiguous));
        Assert.That(_status, Is.EqualTo(NotificationStatus.Submitted));
        Assert.That(_sender.Sends, Is.Empty);
    }

    [Test]
    public async Task DispatchAsync_KnownTransientFailure_RetriesIdenticalSnapshotUntilAttemptLimit()
    {
        RecommendationEmail snapshot = new("synthetic", "fixed body", RecipientAddresses: ["snapshot@synthetic.example"]);
        EmailDeliveryWork work = new(Guid.NewGuid(), () => snapshot);
        EmailDeliveryDispatcher dispatcher = Dispatcher();
        _sender.SendResults.Enqueue(new EmailSendResult(EmailSendOutcome.TransientFailure, null, "http_429"));
        _sender.SendResults.Enqueue(new EmailSendResult(EmailSendOutcome.TransientFailure, null, "http_429"));
        await dispatcher.DispatchAsync(work, CancellationToken.None);
        await dispatcher.DispatchAsync(work, CancellationToken.None);

        EmailDispatchResult exhausted = await dispatcher.DispatchAsync(work, CancellationToken.None);

        Assert.That(_sender.Sends, Has.Count.EqualTo(2));
        Assert.That(_sender.Sends.All(send => ReferenceEquals(send.Email, snapshot)), Is.True);
        Assert.That(exhausted.Outcome, Is.EqualTo(DispatchItemOutcome.Suppressed));
    }

    [Test]
    public async Task DispatchAsync_LostClaim_DoesNotRenderOrSend()
    {
        _claimable = false;
        EmailDispatchResult result = await Dispatcher().DispatchAsync(new EmailDeliveryWork(Guid.NewGuid(),
            () => throw new InvalidOperationException("Unclaimed message must not render.")), CancellationToken.None);
        Assert.That(result.Outcome, Is.EqualTo(DispatchItemOutcome.ClaimSkipped));
        Assert.That(_sender.Sends, Is.Empty);
    }

    public Task<NotificationClaim?> TryClaimAsync(Guid deliveryId, Guid claimToken, Instant claimedUntil, Instant now, CancellationToken cancellationToken)
    {
        return Task.FromResult(_claimable ? new NotificationClaim(_status, _operation) : null);
    }

    public Task<BeginSubmissionResult> TryBeginSubmissionAsync(Guid deliveryId, Guid claimToken, string providerOperationId, int maxAttempts, Instant now, CancellationToken cancellationToken)
    {
        if (_attempts >= maxAttempts)
        {
            return Task.FromResult(BeginSubmissionResult.AttemptsExhausted);
        }

        _operation = providerOperationId;
        return Task.FromResult(BeginSubmissionResult.Began);
    }

    public Task<bool> RecordOutcomeAsync(EmailDeliveryOutcome outcome, CancellationToken cancellationToken)
    {
        if (_loseOutcome)
        {
            return Task.FromResult(false);
        }

        _status = outcome.Status;
        if (outcome.AttemptOutcome == NotificationAttemptOutcome.Failed)
        {
            _operation = null;
        }

        _attempts++;
        return Task.FromResult(true);
    }

    private EmailDeliveryDispatcher Dispatcher()
    {
        return new EmailDeliveryDispatcher(this, _sender, new FakeClock(Instant.FromUtc(2026, 1, 11, 18, 0)), 120, 2);
    }
}
