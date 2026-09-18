using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

[TestFixture]
public sealed class RegisterWatchedInstrumentHandlerTests
{
    [Test]
    public async Task HandleAsync_WithValidCommand_UsesClockAndStoresInstrument()
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = new(clock, store);
        RegisterWatchedInstrument command = new(
            Guid.Parse("a34b2207-fc21-4226-91b2-47eb4a40bde1"),
            "demo-2",
            "xtest",
            "gbp",
            60);

        Result<WatchedInstrument> result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.SameAs(store.AddedInstrument));
            Assert.That(result.Value.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(result.Value.Exchange, Is.EqualTo("XTEST"));
            Assert.That(result.Value.QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(result.Value.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(result.Value.CreatedAt, Is.EqualTo(now));
            Assert.That(result.Value.LastChangedAt, Is.EqualTo(now));
            Assert.That(result.Value.MonitoringState, Is.EqualTo(MonitoringState.Configured));
        });
    }

    [Test]
    public async Task HandleAsync_WithInvalidCommand_ReturnsErrorAndSkipsStore()
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = new(clock, store);
        RegisterWatchedInstrument command = new(
            Guid.Empty,
            "demo-2",
            "xtest",
            "gbp",
            60);

        Result<WatchedInstrument> result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.IdRequired));
            Assert.That(store.AddedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenCancelled_PropagatesCancellation()
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = new(clock, store);
        RegisterWatchedInstrument command = new(
            Guid.Parse("a34b2207-fc21-4226-91b2-47eb4a40bde1"),
            "demo-2",
            "xtest",
            "gbp",
            60);
        CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync(command, cancellation.Token));
    }
}
