using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.Tests.WatchedInstruments.Register;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Application.WatchedInstruments.Settings;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Settings;

[TestFixture]
public sealed class UpdateWatchedInstrumentSettingsHandlerTests
{
    private static readonly Guid InstrumentId =
        Guid.Parse("4b6e8f2a-1c3d-4e5f-9a8b-7c6d5e4f3a2b");
    private static readonly byte[] Token = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly Instant Now = Instant.FromUtc(2026, 10, 5, 9, 30);

    [Test]
    public async Task HandleAsync_WithoutToken_ReturnsPreconditionRequired()
    {
        CapturingWatchedInstrumentStore store = new();
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Monitored,
                120,
                null),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(WatchedInstrumentErrors.ConcurrencyTokenRequired));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.PreconditionRequired));
            Assert.That(store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithMismatchedToken_ReturnsPreconditionFailed()
    {
        CapturingWatchedInstrumentStore store = new();
        store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Monitored,
                120,
                [9, 9, 9, 9, 9, 9, 9, 9]),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(WatchedInstrumentErrors.ConcurrentChange));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.PreconditionFailed));
            Assert.That(store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithMatchingTokenAndUnchangedValues_ReturnsSnapshotWithoutSaving()
    {
        CapturingWatchedInstrumentStore store = new();
        store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60);
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Monitored,
                60,
                Token),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Instrument.MonitoringState, Is.EqualTo(MonitoringState.Monitored));
            Assert.That(result.Value.Instrument.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(result.Value.ConcurrencyToken, Is.EqualTo(Token));
            Assert.That(store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithNewInterval_SavesInstrument()
    {
        CapturingWatchedInstrumentStore store = new();
        store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60);
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Monitored,
                120,
                Token),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.SavedInstrument, Is.Not.Null);
            Assert.That(store.SavedInstrument!.SamplingIntervalSeconds, Is.EqualTo(120));
            Assert.That(store.SavedInstrument.MonitoringState, Is.EqualTo(MonitoringState.Monitored));
            Assert.That(store.SavedInstrument.LastChangedAt, Is.EqualTo(Now));
            Assert.That(store.ExpectedToken, Is.EqualTo(Token));
        });
    }

    [Test]
    public async Task HandleAsync_WhenStartingMonitoring_SavesInstrument()
    {
        CapturingWatchedInstrumentStore store = new();
        store.GetSettingsResult = CreateSnapshot(MonitoringState.Configured, 60);
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Monitored,
                60,
                Token),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.SavedInstrument, Is.Not.Null);
            Assert.That(store.SavedInstrument!.MonitoringState, Is.EqualTo(MonitoringState.Monitored));
            Assert.That(store.SavedInstrument.SamplingIntervalSeconds, Is.EqualTo(60));
        });
    }

    [Test]
    public async Task HandleAsync_WhenStoppingMonitoring_SavesInstrument()
    {
        CapturingWatchedInstrumentStore store = new();
        store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60);
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Configured,
                60,
                Token),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.SavedInstrument, Is.Not.Null);
            Assert.That(store.SavedInstrument!.MonitoringState, Is.EqualTo(MonitoringState.Configured));
        });
    }

    [Test]
    public async Task HandleAsync_WhenStoppingAndChangingInterval_SavesBoth()
    {
        CapturingWatchedInstrumentStore store = new();
        store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60);
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Configured,
                300,
                Token),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(store.SavedInstrument!.MonitoringState, Is.EqualTo(MonitoringState.Configured));
            Assert.That(store.SavedInstrument.SamplingIntervalSeconds, Is.EqualTo(300));
        });
    }

    [Test]
    public async Task HandleAsync_WithOutOfRangeInterval_ReturnsValidationError()
    {
        CapturingWatchedInstrumentStore store = new();
        store.GetSettingsResult = CreateSnapshot(MonitoringState.Monitored, 60);
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Monitored,
                0,
                Token),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(WatchedInstrumentErrors.SamplingIntervalOutOfRange));
            Assert.That(store.SavedInstrument, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithUnknownInstrument_ReturnsNotFound()
    {
        CapturingWatchedInstrumentStore store = new();
        store.GetSettingsResult = WatchedInstrumentErrors.SettingsNotFound;
        UpdateWatchedInstrumentSettingsHandler handler = new(
            store,
            new FakeClock(Now));

        Result<WatchedInstrumentSettingsSnapshot> result = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                InstrumentId,
                MonitoringState.Monitored,
                120,
                Token),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.SettingsNotFound));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        });
    }

    private static WatchedInstrumentSettingsSnapshot CreateSnapshot(
        MonitoringState state,
        int intervalSeconds)
    {
        WatchedInstrument instrument = WatchedInstrument.Restore(
            InstrumentId,
            "DEMO-2",
            "XTEST",
            "GBP",
            state,
            intervalSeconds,
            Instant.FromUtc(2026, 1, 2, 9, 30),
            Instant.FromUtc(2026, 1, 2, 9, 30)).Value;

        return new WatchedInstrumentSettingsSnapshot(instrument, Token);
    }
}
