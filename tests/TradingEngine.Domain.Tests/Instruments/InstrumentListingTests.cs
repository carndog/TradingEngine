using NodaTime;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.Instruments;

[TestFixture]
public sealed class InstrumentListingTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 9, 30);

    [Test]
    public void Configure_WithValidValues_NormalizesAndCreates()
    {
        Result<InstrumentListing> result = InstrumentListing.Configure(
            " xtest ",
            "demo-2",
            "eToro",
            4242,
            "demo-2",
            "Test Exchange",
            "Demo Two",
            "gbp",
            ListingUnitSource.Administrator,
            " test-admin ",
            Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Exchange, Is.EqualTo("XTEST"));
            Assert.That(result.Value.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(result.Value.Provider, Is.EqualTo("eToro"));
            Assert.That(result.Value.ProviderInstrumentId, Is.EqualTo(4242));
            Assert.That(result.Value.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(
                result.Value.Source,
                Is.EqualTo(ListingUnitSource.Administrator));
            Assert.That(result.Value.ConfirmedBy, Is.EqualTo("test-admin"));
            Assert.That(result.Value.ConfirmedAt, Is.EqualTo(Now));
            Assert.That(result.Value.LastChangedAt, Is.EqualTo(Now));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Configure_WithMissingExchange_ReturnsError(string? exchange)
    {
        Result<InstrumentListing> result = ConfigureValid(exchange: exchange);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.ExchangeRequired));
        });
    }

    [TestCase("x test")]
    [TestCase("x/test")]
    public void Configure_WithInvalidExchangeCharacters_ReturnsError(string exchange)
    {
        Result<InstrumentListing> result = ConfigureValid(exchange: exchange);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.ExchangeInvalidCharacters));
        });
    }

    [Test]
    public void Configure_WithOverlongExchange_ReturnsError()
    {
        Result<InstrumentListing> result = ConfigureValid(
            exchange: new string('x', 21));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.ExchangeExceedsMaximumLength));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void Configure_WithMissingSymbol_ReturnsError(string? symbol)
    {
        Result<InstrumentListing> result = ConfigureValid(symbol: symbol);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.SymbolRequired));
        });
    }

    [Test]
    public void Configure_WithWhitespaceSymbol_ReturnsError()
    {
        Result<InstrumentListing> result = ConfigureValid(symbol: "DEMO 2");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.SymbolContainsWhitespace));
        });
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void Configure_WithNonPositiveProviderId_ReturnsError(long id)
    {
        Result<InstrumentListing> result = ConfigureValid(providerInstrumentId: id);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.ProviderInstrumentIdInvalid));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Configure_WithMissingNativeUnit_ReturnsRequiredError(string? unit)
    {
        Result<InstrumentListing> result = ConfigureValid(nativeQuoteCurrency: unit);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.NativeUnitRequired));
        });
    }

    [TestCase("GB")]
    [TestCase("TOOLONGUNIT")]
    [TestCase("GB/P")]
    public void Configure_WithMalformedNativeUnit_ReturnsFormatError(string unit)
    {
        Result<InstrumentListing> result = ConfigureValid(nativeQuoteCurrency: unit);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.NativeUnitInvalidFormat));
        });
    }

    [Test]
    public void Configure_WithUndefinedSource_ReturnsError()
    {
        Result<InstrumentListing> result = InstrumentListing.Configure(
            "XTEST",
            "DEMO-2",
            "eToro",
            4242,
            null,
            null,
            null,
            "GBP",
            (ListingUnitSource)99,
            "test-admin",
            Now);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.UnitSourceUndefined));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void Configure_WithMissingConfirmedBy_ReturnsError(string? actor)
    {
        Result<InstrumentListing> result = ConfigureValid(confirmedBy: actor);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.ConfirmedByRequired));
        });
    }

    [Test]
    public void Restore_WithLastChangedBeforeConfirmed_ReturnsError()
    {
        Result<InstrumentListing> result = InstrumentListing.Restore(
            "XTEST",
            "DEMO-2",
            "eToro",
            4242,
            null,
            null,
            null,
            "GBP",
            ListingUnitSource.Administrator,
            "test-admin",
            Now,
            Now.Minus(Duration.FromSeconds(1)));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.LastChangedPrecedesConfirmed));
        });
    }

    [Test]
    public void Restore_WithLastChangedAfterConfirmed_PreservesTimestamps()
    {
        Instant changed = Now.Plus(Duration.FromMinutes(5));
        Result<InstrumentListing> result = InstrumentListing.Restore(
            "XTEST",
            "DEMO-2",
            "eToro",
            4242,
            null,
            null,
            null,
            "GBP",
            ListingUnitSource.ProviderMetadata,
            "test-admin",
            Now,
            changed);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.ConfirmedAt, Is.EqualTo(Now));
            Assert.That(result.Value.LastChangedAt, Is.EqualTo(changed));
            Assert.That(
                result.Value.Source,
                Is.EqualTo(ListingUnitSource.ProviderMetadata));
        });
    }

    [Test]
    public void RefreshProviderMetadata_WithNewValues_UpdatesIdentityAndTimestamps()
    {
        InstrumentListing listing = CreateValid();
        Instant changed = Now.Plus(Duration.FromMinutes(10));

        Result result = listing.RefreshProviderMetadata(
            7777,
            "new-symbol",
            "Other Exchange",
            "Renamed",
            "admin-2",
            changed);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(listing.ProviderInstrumentId, Is.EqualTo(7777));
            Assert.That(listing.ProviderSymbol, Is.EqualTo("new-symbol"));
            Assert.That(listing.ProviderExchangeName, Is.EqualTo("Other Exchange"));
            Assert.That(listing.DisplayName, Is.EqualTo("Renamed"));
            Assert.That(listing.ConfirmedBy, Is.EqualTo("admin-2"));
            Assert.That(listing.LastChangedAt, Is.EqualTo(changed));
            Assert.That(listing.NativeQuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(listing.ConfirmedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void RefreshProviderMetadata_WithEarlierChange_ReturnsError()
    {
        InstrumentListing listing = CreateValid();

        Result result = listing.RefreshProviderMetadata(
            7777,
            null,
            null,
            null,
            "admin-2",
            Now.Minus(Duration.FromSeconds(1)));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.LastChangedPrecedesConfirmed));
            Assert.That(listing.ProviderInstrumentId, Is.EqualTo(4242));
        });
    }

    [TestCase("gbp", "GBP")]
    [TestCase("  gbx  ", "GBX")]
    [TestCase("eur", "EUR")]
    public void NormalizeNativeUnit_WithValidInput_Uppercases(
        string input,
        string expected)
    {
        Result<string> result = InstrumentListing.NormalizeNativeUnit(input);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(expected));
        });
    }

    private static Result<InstrumentListing> ConfigureValid(
        string? exchange = "XTEST",
        string? symbol = "DEMO-2",
        long providerInstrumentId = 4242,
        string? nativeQuoteCurrency = "GBP",
        string? confirmedBy = "test-admin")
    {
        return InstrumentListing.Configure(
            exchange,
            symbol,
            "eToro",
            providerInstrumentId,
            null,
            null,
            null,
            nativeQuoteCurrency,
            ListingUnitSource.Administrator,
            confirmedBy,
            Now);
    }

    private static InstrumentListing CreateValid()
    {
        return ConfigureValid().Value;
    }
}
