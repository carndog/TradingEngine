using Microsoft.Extensions.Options;
using NodaTime;
using TradingEngine.Application.Listings;
using TradingEngine.Application.MarketData;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MarketData;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class CompositeNativeQuoteUnitMapTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 9, 30);

    [Test]
    public async Task ResolveAsync_WhenPersistedAndDeploymentAgree_ReturnsPersisted()
    {
        StubListingConfigurationStore listings = new()
        {
            FindResult = CreatePersisted("GBP")
        };
        CompositeNativeQuoteUnitMap map = CreateMap(
            listings,
            ("XTEST", "DEMO-2", "GBP"));

        Result<NativeQuoteUnitResolution> result = await map.ResolveAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(result.Value.Persisted, Is.Not.Null);
            Assert.That(result.Value.DeploymentCurrency, Is.EqualTo("GBP"));
            Assert.That(result.Value.SourceConflict, Is.False);
        });
    }

    [Test]
    public async Task ResolveAsync_WhenOnlyPersisted_ReturnsPersistedUnit()
    {
        StubListingConfigurationStore listings = new()
        {
            FindResult = CreatePersisted("GBX")
        };
        CompositeNativeQuoteUnitMap map = CreateMap(listings);

        Result<NativeQuoteUnitResolution> result = await map.ResolveAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.NativeQuoteCurrency, Is.EqualTo("GBX"));
            Assert.That(result.Value.Persisted, Is.Not.Null);
            Assert.That(result.Value.DeploymentCurrency, Is.Null);
            Assert.That(result.Value.SourceConflict, Is.False);
        });
    }

    [Test]
    public async Task ResolveAsync_WhenOnlyDeployment_ReturnsDeploymentUnit()
    {
        StubListingConfigurationStore listings = new();
        CompositeNativeQuoteUnitMap map = CreateMap(
            listings,
            ("XTEST", "DEMO-2", "gbp"));

        Result<NativeQuoteUnitResolution> result = await map.ResolveAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(result.Value.Persisted, Is.Null);
            Assert.That(result.Value.DeploymentCurrency, Is.EqualTo("GBP"));
            Assert.That(result.Value.SourceConflict, Is.False);
        });
    }

    [Test]
    public async Task ResolveAsync_WhenNeitherSource_ReturnsNullUnit()
    {
        StubListingConfigurationStore listings = new();
        CompositeNativeQuoteUnitMap map = CreateMap(listings);

        Result<NativeQuoteUnitResolution> result = await map.ResolveAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.NativeQuoteCurrency, Is.Null);
            Assert.That(result.Value.Persisted, Is.Null);
            Assert.That(result.Value.DeploymentCurrency, Is.Null);
            Assert.That(result.Value.SourceConflict, Is.False);
        });
    }

    [Test]
    public async Task ResolveAsync_WhenSourcesDisagree_ReturnsSourceConflict()
    {
        StubListingConfigurationStore listings = new()
        {
            FindResult = CreatePersisted("USD")
        };
        CompositeNativeQuoteUnitMap map = CreateMap(
            listings,
            ("XTEST", "DEMO-2", "GBP"));

        Result<NativeQuoteUnitResolution> result = await map.ResolveAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.SourceConflict, Is.True);
            Assert.That(result.Value.NativeQuoteCurrency, Is.Null);
            Assert.That(result.Value.Persisted, Is.Not.Null);
            Assert.That(result.Value.DeploymentCurrency, Is.EqualTo("GBP"));
        });
    }

    [Test]
    public async Task ResolveAsync_WhenPersistedFails_ReturnsStoreError()
    {
        StubListingConfigurationStore listings = new()
        {
            FindResult = InstrumentListingErrors.ConcurrentChange
        };
        CompositeNativeQuoteUnitMap map = CreateMap(listings);

        Result<NativeQuoteUnitResolution> result = await map.ResolveAsync(
            "XTEST",
            "DEMO-2",
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
    public async Task ResolveAsync_WhenSymbolDiffersOnlyInCase_FindsDeploymentMapping()
    {
        StubListingConfigurationStore listings = new();
        CompositeNativeQuoteUnitMap map = CreateMap(
            listings,
            ("XTEST", "demo-2", "GBP"));

        Result<NativeQuoteUnitResolution> result = await map.ResolveAsync(
            "XTEST",
            "DEMO-2",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.DeploymentCurrency, Is.EqualTo("GBP"));
    }

    private static CompositeNativeQuoteUnitMap CreateMap(
        StubListingConfigurationStore listings,
        params (string Exchange, string Symbol, string Currency)[] deployment)
    {
        EToroMarketDataOptions options = new();
        foreach ((string exchange, string symbol, string currency) in deployment)
        {
            if (options.QuoteCurrencyMap.TryGetValue(
                    exchange,
                    out Dictionary<string, string>? symbols) is false)
            {
                symbols = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                options.QuoteCurrencyMap[exchange] = symbols;
            }

            symbols[symbol] = currency;
        }

        return new CompositeNativeQuoteUnitMap(listings, Options.Create(options));
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
