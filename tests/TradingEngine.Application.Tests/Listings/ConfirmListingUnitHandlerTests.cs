using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Tests.PriceCollection;
using TradingEngine.Application.Tests.WatchedInstruments.Register;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.Listings;

[TestFixture]
public sealed class ConfirmListingUnitHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 9, 30);
    private static readonly Guid InstrumentId =
        Guid.Parse("4b6e8f2a-1c3d-4e5f-9a8b-7c6d5e4f3a2b");

    [Test]
    public async Task HandleAsync_WithAdministratorUnit_SavesConfirmedListing()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution(
                "GBP",
                CreatePersistedSnapshot("GBP"),
                "GBP",
                false)
        };
        FakeClock clock = new(Now);
        ConfirmListingUnitHandler handler = CreateHandler(
            instruments,
            listings,
            quoteUnits: quoteUnits,
            clock: clock);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "gbp", null, "test-admin"),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value.ResolutionState,
                Is.EqualTo(ListingResolutionState.Resolved));
            Assert.That(
                result.Value.UnitState,
                Is.EqualTo(ListingUnitState.Confirmed));
            Assert.That(listings.SavedUpsert, Is.Not.Null);
            Assert.That(listings.SavedUpsert!.Exchange, Is.EqualTo("XTEST"));
            Assert.That(listings.SavedUpsert.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(listings.SavedUpsert.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(
                listings.SavedUpsert.Source,
                Is.EqualTo(ListingUnitSource.Administrator));
            Assert.That(listings.SavedUpsert.ConfirmedBy, Is.EqualTo("test-admin"));
            Assert.That(listings.SavedUpsert.ConfirmedAt, Is.EqualTo(Now));
            Assert.That(listings.ExpectedToken, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WithProviderSuppliedUnit_SavesProviderMetadataSource()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        StubMarketDataProvider provider = new()
        {
            ListingResponder = (query, _) => Task.FromResult<Result<MarketListing>>(
                new MarketListing(
                    "eToro",
                    4242,
                    query.Symbol,
                    "Test Exchange",
                    5,
                    "Demo Two",
                    "GBP"))
        };
        ConfirmListingUnitHandler handler = CreateHandler(
            instruments,
            listings,
            provider);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, null, null, "test-admin"),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(listings.SavedUpsert, Is.Not.Null);
            Assert.That(
                listings.SavedUpsert!.Source,
                Is.EqualTo(ListingUnitSource.ProviderMetadata));
            Assert.That(listings.SavedUpsert.NativeQuoteCurrency, Is.EqualTo("GBP"));
        });
    }

    [Test]
    public async Task HandleAsync_WithNoUnitAnywhere_ReturnsNativeUnitRequired()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        ConfirmListingUnitHandler handler = CreateHandler(instruments, listings);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, null, null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(InstrumentListingErrors.NativeUnitRequired));
            Assert.That(listings.SavedUpsert, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenProviderUnitDiffersFromRequest_ReturnsQuoteConflict()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        StubMarketDataProvider provider = new()
        {
            ListingResponder = (query, _) => Task.FromResult<Result<MarketListing>>(
                new MarketListing(
                    "eToro",
                    4242,
                    query.Symbol,
                    "Test Exchange",
                    5,
                    "Demo Two",
                    "USD"))
        };
        ConfirmListingUnitHandler handler = CreateHandler(
            instruments,
            listings,
            provider);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "GBP", null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo(MarketDataErrors.QuoteCurrencyConflict.Code));
            Assert.That(listings.SavedUpsert, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenUnitDiffersFromInstrument_ReturnsQuoteConflict()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        ConfirmListingUnitHandler handler = CreateHandler(instruments, listings);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "USD", null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo(MarketDataErrors.QuoteCurrencyConflict.Code));
            Assert.That(listings.SavedUpsert, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenSourcesConflict_ReturnsSourceConflictError()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution("USD", null, "USD", true)
        };
        ConfirmListingUnitHandler handler = CreateHandler(
            instruments,
            listings,
            quoteUnits: quoteUnits);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "GBP", null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo(MarketDataErrors.QuoteCurrencySourceConflict.Code));
            Assert.That(listings.SavedUpsert, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenDeploymentDiffers_ReturnsSourceConflictError()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution("USD", null, "USD", false)
        };
        ConfirmListingUnitHandler handler = CreateHandler(
            instruments,
            listings,
            quoteUnits: quoteUnits);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "GBP", null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo(MarketDataErrors.QuoteCurrencySourceConflict.Code));
            Assert.That(listings.SavedUpsert, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenProviderCannotResolve_ReturnsResolutionError()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        StubMarketDataProvider provider = new()
        {
            ListingResponder = (_, _) => Task.FromResult<Result<MarketListing>>(
                MarketDataErrors.InstrumentMappingUnresolved)
        };
        ConfirmListingUnitHandler handler = CreateHandler(
            instruments,
            listings,
            provider);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "GBP", null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(MarketDataErrors.InstrumentMappingUnresolved));
            Assert.That(listings.SavedUpsert, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_WhenInstrumentMissing_ReturnsSettingsNotFound()
    {
        CapturingWatchedInstrumentStore instruments = new();
        StubListingConfigurationStore listings = new();
        ConfirmListingUnitHandler handler = CreateHandler(instruments, listings);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "GBP", null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(WatchedInstrumentErrors.SettingsNotFound));
        });
    }

    [Test]
    public async Task HandleAsync_PassesExpectedConcurrencyTokenToStore()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution("GBP", null, "GBP", false)
        };
        ConfirmListingUnitHandler handler = CreateHandler(
            instruments,
            listings,
            quoteUnits: quoteUnits);
        byte[] token = [1, 2, 3, 4];

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "GBP", token, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(listings.ExpectedToken, Is.EqualTo(token));
        });
    }

    [Test]
    public async Task HandleAsync_WhenStoreRejectsSave_ReturnsStoreError()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new()
        {
            SaveResult = InstrumentListingErrors.ConcurrentChange
        };
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution("GBP", null, "GBP", false)
        };
        ConfirmListingUnitHandler handler = CreateHandler(
            instruments,
            listings,
            quoteUnits: quoteUnits);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "GBP", null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.ConcurrentChange));
        });
    }

    [Test]
    public async Task HandleAsync_WithInvalidUnitFormat_ReturnsValidationError()
    {
        CapturingWatchedInstrumentStore instruments = CreateInstruments();
        StubListingConfigurationStore listings = new();
        ConfirmListingUnitHandler handler = CreateHandler(instruments, listings);

        Result<ListingStatusInfo> result = await handler.HandleAsync(
            new ConfirmListingUnit(InstrumentId, "!!", null, "test-admin"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.NativeUnitInvalidFormat));
            Assert.That(listings.SavedUpsert, Is.Null);
        });
    }

    private static ConfirmListingUnitHandler CreateHandler(
        CapturingWatchedInstrumentStore instruments,
        StubListingConfigurationStore listings,
        StubMarketDataProvider? provider = null,
        StubNativeQuoteUnitMap? quoteUnits = null,
        FakeClock? clock = null)
    {
        StubNativeQuoteUnitMap units = quoteUnits ?? StubNativeQuoteUnitMap.Unconfigured();
        return new ConfirmListingUnitHandler(
            instruments,
            listings,
            provider ?? new StubMarketDataProvider(),
            units,
            new ListingStatusEvaluator(units),
            clock ?? new FakeClock(Now));
    }

    private static ListingConfigurationSnapshot CreatePersistedSnapshot(string unit)
    {
        InstrumentListing listing = InstrumentListing.Restore(
            "XTEST",
            "DEMO-2",
            "eToro",
            4242,
            "DEMO-2",
            "Test Exchange",
            "Demo Two",
            unit,
            ListingUnitSource.Administrator,
            "test-admin",
            Now,
            Now).Value;

        return new ListingConfigurationSnapshot(listing, [9, 9, 9, 9]);
    }

    private static CapturingWatchedInstrumentStore CreateInstruments()
    {
        WatchedInstrument instrument = WatchedInstrument.Restore(
            InstrumentId,
            "DEMO-2",
            "XTEST",
            "GBP",
            MonitoringState.Configured,
            60,
            Now,
            Now).Value;

        return new CapturingWatchedInstrumentStore
        {
            GetSettingsResult = new WatchedInstrumentSettingsSnapshot(
                instrument,
                [1, 2, 3, 4])
        };
    }
}
