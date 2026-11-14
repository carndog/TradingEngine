using NodaTime;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Tests.Listings;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Status;

[TestFixture]
public sealed class ListingStatusEvaluatorTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 9, 30);

    [Test]
    public async Task EvaluateAsync_WhenPersistedListingExists_ReturnsConfirmed()
    {
        ListingConfigurationSnapshot persisted = CreatePersisted("GBP");
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution("GBP", persisted, "GBP", false)
        };
        ListingStatusEvaluator evaluator = new(quoteUnits);

        Result<ListingStatusInfo> result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.UnitState, Is.EqualTo(ListingUnitState.Confirmed));
            Assert.That(result.Value.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(
                result.Value.UnitSource,
                Is.EqualTo(ListingUnitSource.Administrator));
            Assert.That(result.Value.DeploymentConfigured, Is.True);
            Assert.That(result.Value.Listing, Is.SameAs(persisted.Listing));
            Assert.That(
                result.Value.ConcurrencyToken,
                Is.EqualTo(persisted.ConcurrencyToken));
            Assert.That(result.Value.ResolutionState, Is.Null);
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenOnlyDeploymentMappingExists_ReturnsDeployment()
    {
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution("GBP", null, "GBP", false)
        };
        ListingStatusEvaluator evaluator = new(quoteUnits);

        Result<ListingStatusInfo> result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.UnitState, Is.EqualTo(ListingUnitState.Deployment));
            Assert.That(result.Value.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(
                result.Value.UnitSource,
                Is.EqualTo(ListingUnitSource.Deployment));
            Assert.That(result.Value.DeploymentConfigured, Is.True);
            Assert.That(result.Value.Listing, Is.Null);
            Assert.That(result.Value.ConcurrencyToken, Is.Null);
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenNoConfigurationExists_ReturnsUnconfirmed()
    {
        StubNativeQuoteUnitMap quoteUnits = StubNativeQuoteUnitMap.Unconfigured();
        ListingStatusEvaluator evaluator = new(quoteUnits);

        Result<ListingStatusInfo> result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.UnitState, Is.EqualTo(ListingUnitState.Unconfirmed));
            Assert.That(result.Value.NativeQuoteCurrency, Is.Null);
            Assert.That(result.Value.UnitSource, Is.Null);
            Assert.That(result.Value.DeploymentConfigured, Is.False);
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenSourcesConflict_ReturnsConflict()
    {
        ListingConfigurationSnapshot persisted = CreatePersisted("USD");
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution(null, persisted, "GBP", true)
        };
        ListingStatusEvaluator evaluator = new(quoteUnits);

        Result<ListingStatusInfo> result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.UnitState, Is.EqualTo(ListingUnitState.Conflict));
            Assert.That(result.Value.NativeQuoteCurrency, Is.EqualTo("USD"));
            Assert.That(
                result.Value.UnitSource,
                Is.EqualTo(ListingUnitSource.Administrator));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenResolutionFails_ReturnsError()
    {
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = MarketDataErrors.QuoteCurrencyUnconfigured
        };
        ListingStatusEvaluator evaluator = new(quoteUnits);

        Result<ListingStatusInfo> result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(MarketDataErrors.QuoteCurrencyUnconfigured));
        });
    }

    private static ListingConfigurationSnapshot CreatePersisted(string unit)
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
}
