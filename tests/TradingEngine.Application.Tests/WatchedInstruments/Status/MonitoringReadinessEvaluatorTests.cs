using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Tests.Listings;
using TradingEngine.Application.Tests.PriceCollection;
using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.WatchedInstruments.Status;

[TestFixture]
public sealed class MonitoringReadinessEvaluatorTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 2, 9, 30);

    [Test]
    public async Task EvaluateAsync_WhenUnitMatchesAndSessionConfigured_ReturnsSuccess()
    {
        StubNativeQuoteUnitMap quoteUnits = StubNativeQuoteUnitMap.Configured("GBP");
        StubMarketSessionPolicy sessions = new();
        MonitoringReadinessEvaluator evaluator = CreateEvaluator(
            quoteUnits,
            sessions);

        Result result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            "GBP",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(quoteUnits.Calls, Has.Count.EqualTo(1));
            Assert.That(quoteUnits.Calls[0], Is.EqualTo(("XTEST", "DEMO-2")));
            Assert.That(sessions.Calls, Has.Count.EqualTo(1));
            Assert.That(sessions.Calls[0].Exchange, Is.EqualTo("XTEST"));
            Assert.That(sessions.Calls[0].At, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenSourcesConflict_ReturnsSourceConflictError()
    {
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = new NativeQuoteUnitResolution("USD", null, "USD", true)
        };
        MonitoringReadinessEvaluator evaluator = CreateEvaluator(quoteUnits);

        Result result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            "GBP",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo(MarketDataErrors.QuoteCurrencySourceConflict.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenUnitUnconfigured_ReturnsUnconfiguredError()
    {
        StubNativeQuoteUnitMap quoteUnits = StubNativeQuoteUnitMap.Unconfigured();
        MonitoringReadinessEvaluator evaluator = CreateEvaluator(quoteUnits);

        Result result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            "GBP",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo(MarketDataErrors.QuoteCurrencyUnconfigured.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenUnitDiffers_ReturnsQuoteCurrencyConflict()
    {
        StubNativeQuoteUnitMap quoteUnits = StubNativeQuoteUnitMap.Configured("USD");
        MonitoringReadinessEvaluator evaluator = CreateEvaluator(quoteUnits);

        Result result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            "GBP",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo(MarketDataErrors.QuoteCurrencyConflict.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenSessionUnconfigured_ReturnsSessionError()
    {
        StubNativeQuoteUnitMap quoteUnits = StubNativeQuoteUnitMap.Configured("GBP");
        StubMarketSessionPolicy sessions = new()
        {
            Responder = (_, _) => ExchangeSessionStatus.Unconfigured
        };
        MonitoringReadinessEvaluator evaluator = CreateEvaluator(
            quoteUnits,
            sessions);

        Result result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            "GBP",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error.Code,
                Is.EqualTo(PriceCollectionErrors.ExchangeSessionUnconfigured.Code));
        });
    }

    [Test]
    public async Task EvaluateAsync_WhenSessionClosed_ReturnsSuccess()
    {
        StubNativeQuoteUnitMap quoteUnits = StubNativeQuoteUnitMap.Configured("GBP");
        StubMarketSessionPolicy sessions = new()
        {
            Responder = (_, _) => ExchangeSessionStatus.Closed
        };
        MonitoringReadinessEvaluator evaluator = CreateEvaluator(
            quoteUnits,
            sessions);

        Result result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            "GBP",
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task EvaluateAsync_WhenQuoteUnitsFail_ReturnsResolutionError()
    {
        StubNativeQuoteUnitMap quoteUnits = new()
        {
            Result = MarketDataErrors.QuoteCurrencyConflict
        };
        MonitoringReadinessEvaluator evaluator = CreateEvaluator(quoteUnits);

        Result result = await evaluator.EvaluateAsync(
            "XTEST",
            "DEMO-2",
            "GBP",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(MarketDataErrors.QuoteCurrencyConflict));
        });
    }

    private static MonitoringReadinessEvaluator CreateEvaluator(
        StubNativeQuoteUnitMap quoteUnits,
        StubMarketSessionPolicy? sessions = null)
    {
        return new MonitoringReadinessEvaluator(
            quoteUnits,
            sessions ?? new StubMarketSessionPolicy(),
            new FakeClock(Now));
    }
}
