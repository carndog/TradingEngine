using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

[TestFixture]
public sealed class RegisterWatchedInstrumentHandlerTests
{
    [Test]
    public async Task HandleAsync_WithValidCommand_UsesClockAndStoresRegistration()
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = new(clock, store);
        ChartAnalysisDefinition definition = CreateDefinition();
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            definition);

        Result<WatchedInstrument> result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Id, Is.EqualTo(CapturingWatchedInstrumentStore.DefaultGeneratedId));
            Assert.That(store.AddedRegistration, Is.Not.Null);
            Assert.That(store.AddedRegistration!.Fields.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(store.AddedRegistration.Fields.Exchange, Is.EqualTo("XTEST"));
            Assert.That(store.AddedRegistration.Fields.QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(store.AddedRegistration.Fields.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(store.AddedRegistration.MonitoringState, Is.EqualTo(MonitoringState.Configured));
            Assert.That(store.AddedRegistration.CreatedAt, Is.EqualTo(now));
            Assert.That(store.AddedRegistration.Definition, Is.SameAs(definition));
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
            "",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition());

        Result<WatchedInstrument> result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.SymbolRequired));
            Assert.That(store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithUndefinedMonitoringState_ReturnsErrorAndSkipsStore()
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = new(clock, store);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            (MonitoringState)99,
            CreateDefinition());

        Result<WatchedInstrument> result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.MonitoringStateUndefined));
            Assert.That(store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenStoreRejectsConfiguration_ReturnsStoreError()
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new(
            WatchedInstrumentErrors.DuplicateBusinessKey);
        RegisterWatchedInstrumentHandler handler = new(clock, store);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition());

        Result<WatchedInstrument> result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.DuplicateBusinessKey));
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
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition());
        CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync(command, cancellation.Token));
    }

    [Test]
    public async Task HandleAsync_WithMonitoredState_StartsMonitoring()
    {
        Instant now = Instant.FromUtc(2026, 1, 2, 9, 30);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = new(clock, store);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Monitored,
            CreateDefinition());

        Result<WatchedInstrument> result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.MonitoringState, Is.EqualTo(MonitoringState.Monitored));
            Assert.That(result.Value.LastChangedAt, Is.EqualTo(now));
            Assert.That(
                store.AddedRegistration!.MonitoringState,
                Is.EqualTo(MonitoringState.Monitored));
        });
    }

    private static ChartAnalysisDefinition CreateDefinition()
    {
        return ChartAnalysisDefinition.Create(
            4,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    95m,
                    100m,
                    105m,
                    [
                        ChartCondition.Create(
                            ChartConditionType.BuyZone,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                        ChartCondition.Create(
                            ChartConditionType.SupportLoss,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value
                    ]).Value
            ],
            []).Value;
    }
}
