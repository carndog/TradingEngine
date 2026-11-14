using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Tests.Listings;
using TradingEngine.Application.Tests.PriceCollection;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

[TestFixture]
public sealed class RegisterWatchedInstrumentHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 9, 30);

    [Test]
    public async Task HandleAsync_WithValidCommand_UsesClockAndStoresRegistration()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        StubListingConfigurationStore listings = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(
            clock,
            store,
            listings: listings);
        ChartAnalysisDefinition definition = CreateDefinition();
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            definition,
            "test-admin");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value.Instrument.Id,
                Is.EqualTo(CapturingWatchedInstrumentStore.DefaultGeneratedId));
            Assert.That(store.AddedRegistration, Is.Not.Null);
            Assert.That(store.AddedRegistration!.Fields.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(store.AddedRegistration.Fields.Exchange, Is.EqualTo("XTEST"));
            Assert.That(store.AddedRegistration.Fields.QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(store.AddedRegistration.Fields.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(store.AddedRegistration.MonitoringState, Is.EqualTo(MonitoringState.Configured));
            Assert.That(store.AddedRegistration.CreatedAt, Is.EqualTo(Now));
            Assert.That(store.AddedRegistration.Definition, Is.SameAs(definition));
            Assert.That(result.Value.Instrument.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(result.Value.Instrument.Exchange, Is.EqualTo("XTEST"));
            Assert.That(result.Value.Instrument.QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(result.Value.Instrument.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(result.Value.Instrument.CreatedAt, Is.EqualTo(Now));
            Assert.That(result.Value.Instrument.LastChangedAt, Is.EqualTo(Now));
            Assert.That(
                result.Value.Instrument.MonitoringState,
                Is.EqualTo(MonitoringState.Configured));
            Assert.That(
                result.Value.Listing.ResolutionState,
                Is.EqualTo(ListingResolutionState.Resolved));
            Assert.That(result.Value.Listing.UnitState, Is.EqualTo(ListingUnitState.Unconfirmed));
            Assert.That(listings.SavedUpsert, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithConfirmedNativeUnit_PersistsListingConfiguration()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        StubListingConfigurationStore listings = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(
            clock,
            store,
            listings: listings);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition(),
            "test-admin",
            "gbp");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(listings.SavedUpsert, Is.Not.Null);
            Assert.That(listings.SavedUpsert!.Exchange, Is.EqualTo("XTEST"));
            Assert.That(listings.SavedUpsert.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(listings.SavedUpsert.Provider, Is.EqualTo("eToro"));
            Assert.That(listings.SavedUpsert.ProviderInstrumentId, Is.EqualTo(4242));
            Assert.That(listings.SavedUpsert.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(listings.SavedUpsert.Source, Is.EqualTo(ListingUnitSource.Administrator));
            Assert.That(listings.SavedUpsert.ConfirmedBy, Is.EqualTo("test-admin"));
            Assert.That(listings.SavedUpsert.ConfirmedAt, Is.EqualTo(Now));
            Assert.That(result.Value.Listing.UnitState, Is.EqualTo(ListingUnitState.Confirmed));
            Assert.That(result.Value.Listing.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(
                result.Value.Listing.UnitSource,
                Is.EqualTo(ListingUnitSource.Administrator));
        });
    }

    [Test]
    public async Task HandleAsync_WhenListingUnknownAndConfigured_RegistersWithNotFoundResolution()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        StubMarketDataProvider provider = new()
        {
            ListingResponder = (_, _) =>
                Task.FromResult<Result<MarketListing>>(MarketDataErrors.InstrumentMappingUnresolved)
        };
        RegisterWatchedInstrumentHandler handler = CreateHandler(clock, store, provider);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition(),
            "test-admin");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value.Listing.ResolutionState,
                Is.EqualTo(ListingResolutionState.NotFound));
            Assert.That(
                result.Value.Listing.ResolutionDetailCode,
                Is.EqualTo(MarketDataErrors.InstrumentMappingUnresolved.Code));
            Assert.That(store.AddedRegistration, Is.Not.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenListingUnknownAndMonitored_ReturnsResolutionError()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        StubMarketDataProvider provider = new()
        {
            ListingResponder = (_, _) =>
                Task.FromResult<Result<MarketListing>>(MarketDataErrors.InstrumentMappingUnresolved)
        };
        RegisterWatchedInstrumentHandler handler = CreateHandler(
            clock,
            store,
            provider,
            quoteUnits: StubNativeQuoteUnitMap.Configured("GBP"));
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Monitored,
            CreateDefinition(),
            "test-admin",
            "gbp");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(MarketDataErrors.InstrumentMappingUnresolved));
            Assert.That(store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenMonitoredAndUnitUnconfirmed_ReturnsUnconfiguredError()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(
            clock,
            store,
            quoteUnits: StubNativeQuoteUnitMap.Unconfigured());
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Monitored,
            CreateDefinition(),
            "test-admin");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo(MarketDataErrors.QuoteCurrencyUnconfigured.Code));
            Assert.That(store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenConfirmedUnitConflictsWithDeployment_ReturnsSourceConflict()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution("USD", null, "USD", false)
        };
        StubListingConfigurationStore listings = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(
            clock,
            store,
            listings: listings,
            quoteUnits: quoteUnits);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition(),
            "test-admin",
            "gbp");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo(MarketDataErrors.QuoteCurrencySourceConflict.Code));
            Assert.That(listings.SavedUpsert, Is.Null);
            Assert.That(store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenConfirmedUnitConflictsWithInstrument_ReturnsQuoteConflict()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        StubListingConfigurationStore listings = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(
            clock,
            store,
            listings: listings,
            quoteUnits: StubNativeQuoteUnitMap.Unconfigured());
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "usd",
            60,
            MonitoringState.Configured,
            CreateDefinition(),
            "test-admin",
            "gbp");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo(MarketDataErrors.QuoteCurrencyConflict.Code));
            Assert.That(listings.SavedUpsert, Is.Null);
            Assert.That(store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithInvalidCommand_ReturnsErrorAndSkipsStore()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(clock, store);
        RegisterWatchedInstrument command = new(
            "",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition(),
            "test-admin");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

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
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(clock, store);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            (MonitoringState)99,
            CreateDefinition(),
            "test-admin");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

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
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new(
            WatchedInstrumentErrors.DuplicateBusinessKey);
        RegisterWatchedInstrumentHandler handler = CreateHandler(clock, store);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition(),
            "test-admin");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(WatchedInstrumentErrors.DuplicateBusinessKey));
        });
    }

    [Test]
    public async Task HandleAsync_WhenCancelled_PropagatesCancellation()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(clock, store);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition(),
            "test-admin");
        CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.HandleAsync(command, cancellation.Token));
    }

    [Test]
    public async Task HandleAsync_WithUnrepresentableClockInstant_ThrowsBeforeStoreCall()
    {
        Instant now = Now.PlusNanoseconds(50);
        FakeClock clock = new(now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(clock, store);
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Configured,
            CreateDefinition(),
            "test-admin");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handler.HandleAsync(command, CancellationToken.None));

        Assert.That(store.AddedRegistration, Is.Null);
    }

    [Test]
    public async Task HandleAsync_WithMonitoredState_StartsMonitoring()
    {
        FakeClock clock = new(Now);
        CapturingWatchedInstrumentStore store = new();
        RegisterWatchedInstrumentHandler handler = CreateHandler(
            clock,
            store,
            quoteUnits: StubNativeQuoteUnitMap.Configured("GBP"));
        RegisterWatchedInstrument command = new(
            "demo-2",
            "xtest",
            "gbp",
            60,
            MonitoringState.Monitored,
            CreateDefinition(),
            "test-admin");

        Result<WatchedInstrumentRegistrationOutcome> result = await handler.HandleAsync(
            command,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.Value.Instrument.MonitoringState,
                Is.EqualTo(MonitoringState.Monitored));
            Assert.That(result.Value.Instrument.LastChangedAt, Is.EqualTo(Now));
            Assert.That(
                store.AddedRegistration!.MonitoringState,
                Is.EqualTo(MonitoringState.Monitored));
        });
    }

    private static RegisterWatchedInstrumentHandler CreateHandler(
        FakeClock clock,
        CapturingWatchedInstrumentStore store,
        StubMarketDataProvider? provider = null,
        StubListingConfigurationStore? listings = null,
        StubNativeQuoteUnitMap? quoteUnits = null,
        StubMarketSessionPolicy? sessionPolicy = null)
    {
        StubNativeQuoteUnitMap units = quoteUnits ?? StubNativeQuoteUnitMap.Unconfigured();
        return new RegisterWatchedInstrumentHandler(
            clock,
            store,
            provider ?? new StubMarketDataProvider(),
            listings ?? new StubListingConfigurationStore(),
            units,
            new MonitoringReadinessEvaluator(
                units,
                sessionPolicy ?? new StubMarketSessionPolicy(),
                clock));
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
