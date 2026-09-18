using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class ChartZoneTests
{
    [Test]
    public void Create_WithValidValues_ReturnsZone()
    {
        Result<ChartZone> result = CreateZone(95m, 100m, 105m);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Id.Value, Is.EqualTo("support-a"));
            Assert.That(result.Value.Lower, Is.EqualTo(95m));
            Assert.That(result.Value.Level, Is.EqualTo(100m));
            Assert.That(result.Value.Upper, Is.EqualTo(105m));
            Assert.That(result.Value.Conditions, Has.Count.EqualTo(1));
        });
    }

    [TestCase(0d)]
    [TestCase(-1d)]
    public void Create_WithNonPositivePrice_ReturnsZoneNonPositivePriceError(double price)
    {
        Result<ChartZone> result = CreateZone((decimal)price, 100m, 105m);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.ZoneNonPositivePrice));
    }

    [Test]
    public void Create_WithInvertedBoundaries_ReturnsZoneInvalidBoundaryOrderError()
    {
        Result<ChartZone> result = CreateZone(105m, 100m, 95m);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.ZoneInvalidBoundaryOrder));
    }

    [Test]
    public void Create_WithLevelOutsideBoundaries_ReturnsZoneInvalidBoundaryOrderError()
    {
        Result<ChartZone> result = CreateZone(95m, 110m, 105m);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.ZoneInvalidBoundaryOrder));
    }

    [Test]
    public void Create_WithNoConditions_ReturnsZoneMissingConditionsError()
    {
        Result<ChartZone> result = ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a").Value,
            95m,
            100m,
            105m,
            []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.ZoneMissingConditions));
    }

    [Test]
    public void Create_WithValidConditions_ReturnsDefensiveCopy()
    {
        ChartCondition[] conditions =
        [
            ChartCondition.Create(
                ChartConditionType.BuyZone,
                ChartAnalysisIdentifier.From("publish-signal").Value).Value
        ];

        Result<ChartZone> result = ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a").Value,
            95m,
            100m,
            105m,
            conditions);
        conditions[0] = ChartCondition.Create(
            ChartConditionType.Breakout,
            ChartAnalysisIdentifier.From("publish-signal").Value).Value;

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Conditions[0].Type, Is.EqualTo(ChartConditionType.BuyZone));
    }

    private static Result<ChartZone> CreateZone(decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a").Value,
            lower,
            level,
            upper,
            [
                ChartCondition.Create(
                    ChartConditionType.BuyZone,
                    ChartAnalysisIdentifier.From("publish-signal").Value).Value
            ]);
    }
}
