using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Tests.Listings;
using TradingEngine.Application.Tests.PriceCollection;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Application.WatchedInstruments.Verify;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Verify;

[TestFixture]
public sealed class VerifyInstrumentHandlerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 12, 0);
    private static readonly VerifyInstrument Command = new("DEMO-2", "XTEST", "USD");
    private StubMarketDataProvider _provider = null!;
    private StubNativeQuoteUnitMap _units = null!;
    private StubMarketSessionPolicy _sessions = null!;
    private ManualTimeProvider _time = null!;
    private VerifyInstrumentHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new StubMarketDataProvider { Responder = (identity, _) => Task.FromResult(Quote(identity)) };
        _units = StubNativeQuoteUnitMap.Configured("USD");
        _sessions = new StubMarketSessionPolicy();
        _time = new ManualTimeProvider();
        FakeClock clock = new(Now);
        _handler = new VerifyInstrumentHandler(_provider, _units,
            new MonitoringReadinessEvaluator(_units, _sessions, clock), _sessions, clock, _time);
    }

    [Test]
    public async Task HandleAsync_ExactListingAndVerifiedUnits_ReturnsIdentityAndQuote()
    {
        Result<InstrumentVerification> result = await _handler.HandleAsync(
            new VerifyInstrument(" demo-2 ", " xtest ", " usd "), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Identity, Is.EqualTo(new MarketInstrumentIdentity("XTEST", "DEMO-2", "USD")));
        Assert.That(result.Value.ResolutionState, Is.EqualTo(ListingResolutionState.Resolved));
        Assert.That(result.Value.Listing!.DisplayName, Is.EqualTo("DEMO-2 Synthetic"));
        Assert.That(result.Value.ReadyForMonitoring, Is.True);
        Assert.That(result.Value.Quote!.RetrievedAt, Is.EqualTo(Now));
        Assert.That(result.Value.Quote.ProviderObservedAt, Is.EqualTo(Now - Duration.FromMinutes(15)));
        Assert.That(result.Value.Quote.Timeliness, Is.EqualTo(MarketQuoteTimeliness.Delayed));
    }

    [TestCase("missing", ListingResolutionState.NotFound)]
    [TestCase("ambiguous", ListingResolutionState.Ambiguous)]
    [TestCase("exchange", ListingResolutionState.UnsupportedVenue)]
    [TestCase("throttled", ListingResolutionState.Unavailable)]
    [TestCase("credentials", ListingResolutionState.Unavailable)]
    public async Task HandleAsync_ResolutionFails_PreservesReasonWithoutRequestingQuote(
        string mode, ListingResolutionState state)
    {
        Error error = mode switch
        {
            "missing" => MarketDataErrors.InstrumentMappingUnresolved,
            "ambiguous" => MarketDataErrors.InstrumentMappingAmbiguous,
            "exchange" => MarketDataErrors.ExchangeMappingUnconfigured,
            "throttled" => MarketDataErrors.ProviderThrottled,
            _ => MarketDataErrors.ProviderCredentialsUnconfigured
        };
        _provider.ListingResponder = (_, _) => Task.FromResult<Result<MarketListing>>(error);

        Result<InstrumentVerification> result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.That(result.Value.ResolutionState, Is.EqualTo(state));
        Assert.That(result.Value.ResolutionDetailCode, Is.EqualTo(error.Code));
        Assert.That(result.Value.ReadyForMonitoring, Is.False);
        Assert.That(_provider.Requests, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("USD")]
    public async Task HandleAsync_UnconfirmedUnit_DoesNotTrustFormOrRequestQuote(string? typedUnit)
    {
        _units.Result = new NativeQuoteUnitResolution(null, null, null, false);

        Result<InstrumentVerification> result = await _handler.HandleAsync(
            Command with { NativeQuoteCurrency = typedUnit }, CancellationToken.None);

        Assert.That(result.Value.ResolutionState, Is.EqualTo(ListingResolutionState.Resolved));
        Assert.That(result.Value.NativeQuoteCurrency, Is.Null);
        Assert.That(result.Value.ReadinessDetailCode, Is.EqualTo(MarketDataErrors.QuoteCurrencyUnconfigured.Code));
        Assert.That(_provider.Requests, Is.Empty);
    }

    [TestCase("quote")]
    [TestCase("native")]
    [TestCase("source")]
    [TestCase("metadata")]
    public async Task HandleAsync_ConflictingUnits_KeepsRecognitionAndBlocksQuote(string mode)
    {
        VerifyInstrument command = Command;
        if (mode == "quote")
        {
            command = command with { QuoteCurrency = "GBP" };
        }
        else if (mode == "native")
        {
            command = command with { NativeQuoteCurrency = "GBP" };
        }
        else if (mode == "source")
        {
            _units.Result = new NativeQuoteUnitResolution("USD", null, "GBP", true);
        }
        else
        {
            _provider.ListingResponder = (query, _) => Task.FromResult<Result<MarketListing>>(
                new MarketListing("eToro", 4242, query.Symbol, "Test Exchange", 5, "Demo", "GBP"));
        }

        Result<InstrumentVerification> result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.That(result.Value.ResolutionState, Is.EqualTo(ListingResolutionState.Resolved));
        Assert.That(result.Value.ReadinessDetailCode, Does.Contain("conflict"));
        Assert.That(result.Value.ReadyForMonitoring, Is.False);
        Assert.That(_provider.Requests, Is.Empty);
    }

    [TestCase(ExchangeSessionStatus.Closed, true)]
    [TestCase(ExchangeSessionStatus.Unconfigured, false)]
    [TestCase(ExchangeSessionStatus.CalendarUnavailable, false)]
    public async Task HandleAsync_SessionBlocksQuotes_SeparatesRecognitionAndReadiness(
        ExchangeSessionStatus status, bool ready)
    {
        _sessions.Responder = (_, _) => status;

        Result<InstrumentVerification> result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.That(result.Value.ResolutionState, Is.EqualTo(ListingResolutionState.Resolved));
        Assert.That(result.Value.ReadyForMonitoring, Is.EqualTo(ready));
        Assert.That(result.Value.Session.Status, Is.EqualTo(status));
        Assert.That(_provider.Requests, Is.Empty);
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task HandleAsync_PartialQuote_RequiresAtLeastOneSide(bool bid, bool ask)
    {
        _provider.Responder = (identity, _) => Task.FromResult<Result<MarketQuote>>(
            Quote(identity).Value with { Bid = bid ? 12m : null, Ask = ask ? 13m : null });

        Result<InstrumentVerification> result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.That(result.Value.Quote is not null, Is.EqualTo(bid || ask));
        Assert.That(result.Value.ReadyForMonitoring, Is.True);
        if (bid is false && ask is false)
        {
            Assert.That(result.Value.QuoteDetailCode, Is.EqualTo(MarketDataErrors.PriceUnavailable.Code));
        }
    }

    [Test]
    public async Task HandleAsync_QuoteFromDifferentListing_DoesNotDisplayIt()
    {
        _provider.Responder = (identity, _) => Task.FromResult<Result<MarketQuote>>(
            Quote(identity).Value with { ProviderInstrumentId = 9999 });

        Result<InstrumentVerification> result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.That(result.Value.Quote, Is.Null);
        Assert.That(result.Value.QuoteDetailCode, Is.EqualTo(MarketDataErrors.ProviderUnavailable.Code));
    }

    [Test]
    public async Task HandleAsync_ProviderThrowsAfterRecognition_PreservesListingAndSanitizesFailure()
    {
        _provider.Responder = (_, _) => throw new InvalidOperationException("synthetic-private-provider-payload");

        Result<InstrumentVerification> result = await _handler.HandleAsync(Command, CancellationToken.None);

        Assert.That(result.Value.ResolutionState, Is.EqualTo(ListingResolutionState.Resolved));
        Assert.That(result.Value.QuoteDetailCode, Is.EqualTo(MarketDataErrors.ProviderUnavailable.Code));
        Assert.That(result.Value.ToString(), Does.Not.Contain("synthetic-private-provider-payload"));
    }

    [Test]
    public async Task HandleAsync_DeadlineExpiresDuringQuote_CancelsAndRetainsRecognisedListing()
    {
        _provider.Responder = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return MarketDataErrors.ProviderUnavailable;
        };

        Task<Result<InstrumentVerification>> pending = _handler.HandleAsync(Command, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(20));
        Result<InstrumentVerification> result = await pending;

        Assert.That(result.Value.ResolutionState, Is.EqualTo(ListingResolutionState.Resolved));
        Assert.That(result.Value.QuoteDetailCode, Is.EqualTo(MarketDataErrors.ProviderTimeout.Code));
    }

    [Test]
    public void HandleAsync_CallerCancels_PropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _handler.HandleAsync(Command, cancellation.Token));
    }

    [Test]
    public async Task HandleAsync_InvalidSymbol_RejectsBeforeProviderCall()
    {
        bool called = false;
        _provider.ListingResponder = (_, _) =>
        {
            called = true;
            return Task.FromResult<Result<MarketListing>>(MarketDataErrors.ProviderUnavailable);
        };

        Result<InstrumentVerification> result = await _handler.HandleAsync(Command with { Symbol = "" }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(called, Is.False);
    }

    private static Result<MarketQuote> Quote(MarketInstrumentIdentity identity) => new MarketQuote(
        identity, "eToro", 4242, 12m, 13m, Now - Duration.FromMinutes(15), Now, MarketQuoteTimeliness.Delayed, null);
}
