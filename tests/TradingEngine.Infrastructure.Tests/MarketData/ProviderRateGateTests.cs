using NodaTime;
using NodaTime.Testing;
using TradingEngine.Infrastructure.MarketData;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class ProviderRateGateTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 30, 12, 0, 0);

    private FakeClock _clock = null!;
    private ManualTimeProvider _timeProvider = null!;
    private ProviderRateGate _gate = null!;
    private ProviderRateLimitPolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock(Now);
        _timeProvider = new ManualTimeProvider(
            delay => _clock.Advance(Duration.FromTimeSpan(delay)));
        _gate = new ProviderRateGate(_clock, _timeProvider);
        _policy = new ProviderRateLimitPolicy("test-quota", 2, TimeSpan.FromSeconds(60));
    }

    [Test]
    public async Task WaitForSlotAsync_WhenUnderLimit_AcquiresImmediately()
    {
        Instant deadline = Now.Plus(Duration.FromMinutes(5));

        bool first = await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        bool second = await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.True);
            Assert.That(_timeProvider.DelayRequests, Is.Empty);
        });
    }

    [Test]
    public async Task WaitForSlotAsync_WhenWindowFull_WaitsForOldestEntryToExpire()
    {
        Instant deadline = Now.Plus(Duration.FromMinutes(5));
        await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);

        bool third = await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(third, Is.True);
            Assert.That(_timeProvider.DelayRequests,
                Is.EqualTo(new[] { TimeSpan.FromSeconds(60) }));
        });
    }

    [Test]
    public async Task WaitForSlotAsync_WhenSlotOpensAfterDeadline_ReturnsFalse()
    {
        Instant deadline = Now.Plus(Duration.FromSeconds(30));
        await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);

        bool third = await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(third, Is.False);
            Assert.That(_timeProvider.DelayRequests, Is.Empty);
        });
    }

    [Test]
    public async Task WaitForSlotAsync_WhenEntriesExpired_ReusesWindowWithoutWaiting()
    {
        Instant deadline = Now.Plus(Duration.FromMinutes(5));
        await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        await _gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        _clock.Advance(Duration.FromSeconds(61));

        bool third = await _gate.WaitForSlotAsync(
            _policy,
            Now.Plus(Duration.FromMinutes(10)),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(third, Is.True);
            Assert.That(_timeProvider.DelayRequests, Is.Empty);
        });
    }

    [Test]
    public async Task WaitForSlotAsync_WhenDeadlineExpired_ReturnsFalseWithFreeWindow()
    {
        Instant deadline = Now.Minus(Duration.FromSeconds(1));

        bool result = await _gate.WaitForSlotAsync(
            _policy,
            deadline,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(_timeProvider.DelayRequests, Is.Empty);
        });
    }

    [Test]
    public async Task WaitForSlotAsync_WhenDeadlinePassesWhileQueued_ReturnsFalse()
    {
        ManualTimeProvider deferred = new(autoFire: false);
        ProviderRateGate gate = new(_clock, deferred);
        Instant deadline = Now.Plus(Duration.FromSeconds(90));
        bool first = await gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        bool second = await gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        Task<bool> queued = gate.WaitForSlotAsync(
            _policy,
            deadline,
            CancellationToken.None);

        _clock.Advance(Duration.FromSeconds(91));
        deferred.FireAll();
        bool third = await queued;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.True);
            Assert.That(third, Is.False);
            Assert.That(deferred.DelayRequests,
                Is.EqualTo(new[] { TimeSpan.FromSeconds(60) }));
        });
    }

    [Test]
    public async Task WaitForSlotAsync_WhenSemaphoreWaitExceedsDeadline_ReturnsFalse()
    {
        ManualTimeProvider deferred = new(autoFire: false);
        ProviderRateGate gate = new(_clock, deferred);
        Instant deadline = Now.Plus(Duration.FromSeconds(90));
        bool first = await gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        bool second = await gate.WaitForSlotAsync(_policy, deadline, CancellationToken.None);
        Task<bool> pacing = gate.WaitForSlotAsync(
            _policy,
            deadline,
            CancellationToken.None);
        Task<bool> queued = gate.WaitForSlotAsync(
            _policy,
            deadline,
            CancellationToken.None);

        _clock.Advance(Duration.FromSeconds(91));
        deferred.FireAll();
        bool pacingResult = await pacing;
        bool queuedResult = await queued;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.True);
            Assert.That(pacingResult, Is.False);
            Assert.That(queuedResult, Is.False);
        });
    }

    [Test]
    public void WaitForSlotAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.That(
            async () => await _gate.WaitForSlotAsync(
                _policy,
                Now.Plus(Duration.FromMinutes(5)),
                cancelled.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
