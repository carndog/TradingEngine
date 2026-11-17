using NodaTime;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.SignalTracking;

namespace TradingEngine.Domain.Tests.SignalTracking;

[TestFixture]
public sealed class SignalPriceComparisonCalculatorTests
{
    private static readonly Instant TriggeredAt = Instant.FromUtc(2026, 1, 5, 10, 0);
    private static readonly Instant Later = Instant.FromUtc(2026, 1, 5, 11, 0);

    [TestCase(110, 10)]
    [TestCase(95, -5)]
    [TestCase(100, 0)]
    public void Compare_WithComparableLaterPrice_ReturnsSignedPercentage(decimal latestPrice, decimal expected)
    {
        SignalPriceComparison result = Compare(Baseline(), Observation(latestPrice, latestPrice));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsAvailable, Is.True);
            Assert.That(result.LatestPrice, Is.EqualTo(latestPrice));
            Assert.That(result.PercentageChange, Is.EqualTo(expected));
            Assert.That(result.UnavailableReason, Is.Null);
        });
    }

    [TestCase(PriceBasis.Bid, 90, -10, SignalRangePosition.Below)]
    [TestCase(PriceBasis.Ask, 110, 10, SignalRangePosition.Above)]
    public void Compare_WithDifferentQuoteSides_UsesOriginalBasisForBothMetrics(
        PriceBasis basis,
        decimal expectedPrice,
        decimal expectedPercentage,
        SignalRangePosition expectedPosition)
    {
        SignalPriceBaseline baseline = Baseline() with { PriceBasis = basis, Lower = 95m, Upper = 105m };

        SignalPriceComparison result = Compare(baseline, Observation(90m, 110m));

        Assert.Multiple(() =>
        {
            Assert.That(result.LatestPrice, Is.EqualTo(expectedPrice));
            Assert.That(result.PercentageChange, Is.EqualTo(expectedPercentage));
            Assert.That(result.RangePosition, Is.EqualTo(expectedPosition));
        });
    }

    [Test]
    public void Compare_WithTriggerOutsideOriginalRange_UsesTriggerPriceAsBaseline()
    {
        SignalPriceBaseline baseline = Baseline() with { TriggerPrice = 80m };

        SignalPriceComparison result = Compare(baseline, Observation(100m, 101m));

        Assert.That(result.PercentageChange, Is.EqualTo(25m));
        Assert.That(result.RangePosition, Is.EqualTo(SignalRangePosition.Within));
    }

    [Test]
    public void Compare_WithRisingPriceStillBelowRange_ReturnsPositiveMovementAndBelow()
    {
        SignalPriceBaseline baseline = Baseline() with { TriggerPrice = 80m };

        SignalPriceComparison result = Compare(baseline, Observation(85m, 86m));

        Assert.That(result.PercentageChange, Is.EqualTo(6.25m));
        Assert.That(result.RangePosition, Is.EqualTo(SignalRangePosition.Below));
    }

    [TestCase(89, SignalRangePosition.Below)]
    [TestCase(90, SignalRangePosition.Within)]
    [TestCase(100, SignalRangePosition.Within)]
    [TestCase(110, SignalRangePosition.Within)]
    [TestCase(111, SignalRangePosition.Above)]
    public void Compare_AroundOriginalRange_ClassifiesInclusiveBoundaries(
        decimal latestPrice,
        SignalRangePosition expected)
    {
        SignalPriceComparison result = Compare(Baseline(), Observation(latestPrice, latestPrice));

        Assert.That(result.RangePosition, Is.EqualTo(expected));
    }

    [Test]
    public void Compare_WithFractionalChange_PreservesPrecisionBeforeDisplay()
    {
        SignalPriceBaseline baseline = Baseline() with { TriggerPrice = 3m, Lower = 1m, Upper = 5m };

        SignalPriceComparison result = Compare(baseline, Observation(4m, 4m));

        Assert.That(result.PercentageChange, Is.EqualTo(33.333333333333333333333333333m));
    }

    [Test]
    public void Compare_WithFractionalQuote_PreservesOriginalQuotePrecision()
    {
        SignalPriceComparison result = Compare(Baseline(), Observation(100.000125m, 101m));

        Assert.That(result.LatestPrice, Is.EqualTo(100.000125m));
        Assert.That(result.PercentageChange, Is.EqualTo(0.000125m));
    }

    [Test]
    public void Compare_WithVerySmallPositivePrices_ReturnsPercentageWithoutUnderflow()
    {
        SignalPriceBaseline baseline = Baseline() with
        {
            TriggerPrice = 0.0000000000000000000000000001m,
            Lower = 0.0000000000000000000000000001m,
            Upper = 0.0000000000000000000000000003m
        };

        SignalPriceComparison result = Compare(
            baseline,
            Observation(0.0000000000000000000000000002m, null));

        Assert.That(result.PercentageChange, Is.EqualTo(100m));
        Assert.That(result.RangePosition, Is.EqualTo(SignalRangePosition.Within));
    }

    [Test]
    public void Compare_WithLargePricesAndRepresentablePercentage_AvoidsIntermediateOverflow()
    {
        decimal triggerPrice = decimal.MaxValue / 4m;
        SignalPriceBaseline baseline = Baseline() with
        {
            TriggerPrice = triggerPrice,
            Lower = 1m,
            Upper = decimal.MaxValue
        };

        SignalPriceComparison result = Compare(baseline, Observation(triggerPrice * 2m, null));

        Assert.That(result.PercentageChange, Is.EqualTo(100m));
        Assert.That(result.IsAvailable, Is.True);
    }

    [Test]
    public void Compare_WithSmallRepresentableChangeAtLargeBaseline_DoesNotReportZero()
    {
        SignalPriceBaseline baseline = Baseline() with
        {
            TriggerPrice = decimal.MaxValue,
            Lower = 1m,
            Upper = decimal.MaxValue
        };

        SignalPriceComparison result = Compare(baseline, Observation(decimal.MaxValue - 1m, null));

        Assert.That(result.PercentageChange, Is.LessThan(0m));
        Assert.That(result.PercentageChange, Is.GreaterThan(-0.00000000000000000000000001m));
    }

    [Test]
    public void Compare_WithPercentageOutsideDecimalRange_ReturnsUnavailable()
    {
        SignalPriceBaseline baseline = Baseline() with { TriggerPrice = 0.0000000000000000000000000001m };

        SignalPriceComparison result = Compare(baseline, Observation(decimal.MaxValue, null));

        AssertUnavailable(result, SignalComparisonUnavailableReason.PercentageOutOfRange);
    }

    [Test]
    public void Compare_WithoutLaterObservation_ReturnsUnavailableInsteadOfZero()
    {
        SignalPriceComparison result = Compare(Baseline(), null);

        AssertUnavailable(result, SignalComparisonUnavailableReason.NoLaterObservation);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Compare_WithObservationNotAfterSignal_ReturnsNoLaterObservation(int minutesAfterSignal)
    {
        PriceObservation latest = PriceObservation.Create(
            TriggeredAt.Plus(Duration.FromMinutes(minutesAfterSignal)),
            110m,
            111m,
            "USD").Value;

        SignalPriceComparison result = Compare(Baseline(), latest);

        AssertUnavailable(result, SignalComparisonUnavailableReason.NoLaterObservation);
    }

    [TestCase(PriceBasis.Bid)]
    [TestCase(PriceBasis.Ask)]
    public void Compare_WithOnlyOtherQuoteSide_ReturnsUnavailableWithoutSubstitution(PriceBasis basis)
    {
        SignalPriceBaseline baseline = Baseline() with { PriceBasis = basis };
        PriceObservation latest = basis == PriceBasis.Bid
            ? Observation(null, 110m)
            : Observation(110m, null);

        SignalPriceComparison result = Compare(baseline, latest);

        AssertUnavailable(result, SignalComparisonUnavailableReason.MissingRequiredQuoteSide);
    }

    [TestCase("USD", "EUR")]
    [TestCase("GBP", "GBX")]
    [TestCase("GBX", "GBP")]
    [TestCase("USD", "usd")]
    public void Compare_WithDifferentNativeCurrencyOrUnit_ReturnsUnavailable(
        string triggerCurrency,
        string latestCurrency)
    {
        SignalPriceBaseline baseline = Baseline() with { QuoteCurrency = triggerCurrency };

        SignalPriceComparison result = Compare(baseline, Observation(110m, 111m, latestCurrency));

        AssertUnavailable(result, SignalComparisonUnavailableReason.QuoteCurrencyMismatch);
    }

    [TestCase("USD")]
    [TestCase("GBP")]
    [TestCase("GBX")]
    public void Compare_WithMatchingNativeCurrencyOrUnit_UsesSamePercentage(string currency)
    {
        SignalPriceBaseline baseline = Baseline() with { QuoteCurrency = currency };

        SignalPriceComparison result = Compare(baseline, Observation(110m, 111m, currency));

        Assert.That(result.PercentageChange, Is.EqualTo(10m));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Compare_WithMissingBaselineCurrency_ReturnsUnavailable(string? currency)
    {
        SignalPriceBaseline baseline = Baseline() with { QuoteCurrency = currency };

        SignalPriceComparison result = Compare(baseline, Observation(110m, 111m));

        AssertUnavailable(result, SignalComparisonUnavailableReason.MissingQuoteCurrency);
    }

    [TestCase(0)]
    [TestCase(-100)]
    public void Compare_WithNonPositiveTriggerPrice_ReturnsUnavailable(decimal triggerPrice)
    {
        SignalPriceBaseline baseline = Baseline() with { TriggerPrice = triggerPrice };

        SignalPriceComparison result = Compare(baseline, Observation(110m, 111m));

        AssertUnavailable(result, SignalComparisonUnavailableReason.InvalidTriggerPrice);
    }

    [TestCase(0, 110)]
    [TestCase(-1, 110)]
    [TestCase(90, 90)]
    [TestCase(110, 90)]
    [TestCase(90, 0)]
    public void Compare_WithInvalidOriginalRange_ReturnsUnavailable(decimal lower, decimal upper)
    {
        SignalPriceBaseline baseline = Baseline() with { Lower = lower, Upper = upper };

        SignalPriceComparison result = Compare(baseline, Observation(100m, 101m));

        AssertUnavailable(result, SignalComparisonUnavailableReason.InvalidOriginalRange);
    }

    [Test]
    public void Compare_WithUndefinedPriceBasis_ReturnsUnavailable()
    {
        SignalPriceBaseline baseline = Baseline() with { PriceBasis = (PriceBasis)99 };

        SignalPriceComparison result = Compare(baseline, Observation(100m, 110m));

        AssertUnavailable(result, SignalComparisonUnavailableReason.InvalidPriceBasis);
    }

    [Test]
    public void Compare_WithNullBaseline_ThrowsArgumentNullException()
    {
        SignalPriceComparisonCalculator calculator = new();

        Assert.That(() => calculator.Compare(null!, Observation(110m, 111m)), Throws.ArgumentNullException);
    }

    [Test]
    public void Compare_WithIdenticalInputs_ReturnsEquivalentResults()
    {
        SignalPriceBaseline baseline = Baseline();
        PriceObservation latest = Observation(110m, 111m);

        SignalPriceComparison first = Compare(baseline, latest);
        SignalPriceComparison second = Compare(baseline, latest);

        Assert.That(second, Is.EqualTo(first));
    }

    private static SignalPriceBaseline Baseline()
    {
        return new SignalPriceBaseline(TriggeredAt, 100m, PriceBasis.Bid, "USD", 90m, 110m);
    }

    private static PriceObservation Observation(decimal? bid, decimal? ask, string currency = "USD")
    {
        return PriceObservation.Create(Later, bid, ask, currency).Value;
    }

    private static SignalPriceComparison Compare(SignalPriceBaseline baseline, PriceObservation? latest)
    {
        SignalPriceComparisonCalculator calculator = new();
        return calculator.Compare(baseline, latest);
    }

    private static void AssertUnavailable(
        SignalPriceComparison result,
        SignalComparisonUnavailableReason expectedReason)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.IsAvailable, Is.False);
            Assert.That(result.LatestPrice, Is.Null);
            Assert.That(result.PercentageChange, Is.Null);
            Assert.That(result.RangePosition, Is.Null);
            Assert.That(result.UnavailableReason, Is.EqualTo(expectedReason));
        });
    }
}
