using NodaTime;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class PriceObservationTests
{
    private static readonly Instant ObservedAt = Instant.FromUtc(2026, 11, 1, 10, 0);

    [Test]
    public void Create_WithBothQuoteSides_ReturnsObservation()
    {
        Result<PriceObservation> result = PriceObservation.Create(ObservedAt, 104.5m, 104.75m, "USD");

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.ObservedAt, Is.EqualTo(ObservedAt));
            Assert.That(result.Value.Bid, Is.EqualTo(104.5m));
            Assert.That(result.Value.Ask, Is.EqualTo(104.75m));
            Assert.That(result.Value.QuoteCurrency, Is.EqualTo("USD"));
        });
    }

    [Test]
    public void Create_WithSingleQuoteSide_ReturnsObservation()
    {
        Result<PriceObservation> result = PriceObservation.Create(ObservedAt, 104.5m, null, "GBX");

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Bid, Is.EqualTo(104.5m));
            Assert.That(result.Value.Ask, Is.Null);
        });
    }

    [TestCase(null)]
    [TestCase(" ")]
    public void Create_WithMissingQuoteCurrency_ReturnsQuoteCurrencyRequiredError(string? quoteCurrency)
    {
        Result<PriceObservation> result = PriceObservation.Create(ObservedAt, 104.5m, 104.75m, quoteCurrency);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartEvaluationErrors.QuoteCurrencyRequired));
    }

    [Test]
    public void Create_WithNeitherQuoteSide_ReturnsQuoteSideRequiredError()
    {
        Result<PriceObservation> result = PriceObservation.Create(ObservedAt, null, null, "USD");

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartEvaluationErrors.QuoteSideRequired));
    }

    [TestCase(0d)]
    [TestCase(-1.5d)]
    public void Create_WithNonPositiveBid_ReturnsPriceNotPositiveError(double bid)
    {
        Result<PriceObservation> result = PriceObservation.Create(ObservedAt, (decimal)bid, 104.75m, "USD");

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_evaluation.price_not_positive"));
    }

    [TestCase(0d)]
    [TestCase(-1.5d)]
    public void Create_WithNonPositiveAsk_ReturnsPriceNotPositiveError(double ask)
    {
        Result<PriceObservation> result = PriceObservation.Create(ObservedAt, 104.5m, (decimal)ask, "USD");

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_evaluation.price_not_positive"));
    }

    [Test]
    public void Create_WithPaddedQuoteCurrency_TrimsValue()
    {
        Result<PriceObservation> result = PriceObservation.Create(ObservedAt, 104.5m, 104.75m, "  USD  ");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.QuoteCurrency, Is.EqualTo("USD"));
    }
}
