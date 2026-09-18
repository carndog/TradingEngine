using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class ChartAnalysisDefinitionTests
{
    [Test]
    public void Create_WithValidZones_ReturnsDefinition()
    {
        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(
            4,
            [CreateSupportZone("support-a", 95m, 100m, 105m)],
            [CreateResistanceZone("resistance-a", 120m, 125m, 130m)]);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.PriceScale, Is.EqualTo(4));
            Assert.That(result.Value.SupportZones, Has.Count.EqualTo(1));
            Assert.That(result.Value.ResistanceZones, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Create_WithNoZones_ReturnsMissingZonesError()
    {
        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(4, [], []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.MissingZones));
    }

    [Test]
    public void Create_WithPriceScaleAboveMaximum_ReturnsPriceScaleOutOfRangeError()
    {
        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(
            9,
            [CreateSupportZone("support-a", 95m, 100m, 105m)],
            []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.PriceScaleOutOfRange));
    }

    [Test]
    public void Create_WithNegativePriceScale_ReturnsPriceScaleOutOfRangeError()
    {
        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(
            -1,
            [CreateSupportZone("support-a", 95m, 100m, 105m)],
            []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(ChartAnalysisErrors.PriceScaleOutOfRange));
    }

    [Test]
    public void Create_WithSupportZoneMissingSupportLoss_ReturnsInvalidConditionOrderError()
    {
        ChartZone zone = ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a").Value,
            95m,
            100m,
            105m,
            [ChartCondition.Create(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal").Value).Value]).Value;

        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(4, [zone], []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_analysis.invalid_condition_order"));
    }

    [Test]
    public void Create_WithBreakoutConditionOnSupportZone_ReturnsInvalidConditionOrderError()
    {
        ChartZone zone = ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a").Value,
            95m,
            100m,
            105m,
            [
                ChartCondition.Create(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                ChartCondition.Create(ChartConditionType.Breakout, ChartAnalysisIdentifier.From("publish-signal").Value).Value
            ]).Value;

        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(4, [zone], []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_analysis.invalid_condition_order"));
    }

    [Test]
    public void Create_WithDuplicateZoneIds_ReturnsDuplicateZoneIdError()
    {
        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(
            4,
            [CreateSupportZone("shared-id", 95m, 100m, 105m)],
            [CreateResistanceZone("shared-id", 120m, 125m, 130m)]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_analysis.duplicate_zone_id"));
    }

    [Test]
    public void Create_WithPriceExceedingScale_ReturnsPriceExceedsScaleError()
    {
        ChartZone zone = ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a").Value,
            95.00001m,
            100m,
            105m,
            [
                ChartCondition.Create(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                ChartCondition.Create(ChartConditionType.SupportLoss, ChartAnalysisIdentifier.From("publish-signal").Value).Value
            ]).Value;

        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(4, [zone], []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_analysis.price_exceeds_scale"));
    }

    [Test]
    public void Create_WithMaximumDecimalPrices_AcceptsDefinition()
    {
        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(
            0,
            [CreateSupportZone("support-a", 1m, 2m, decimal.MaxValue)],
            []);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.SupportZones, Has.Count.EqualTo(1));
    }

    [Test]
    public void Create_WithTouchingZones_ReturnsOverlappingZonesError()
    {
        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(
            4,
            [
                CreateSupportZone("support-a", 95m, 100m, 105m),
                CreateSupportZone("support-b", 105m, 110m, 115m)
            ],
            []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_analysis.overlapping_zones"));
    }

    [Test]
    public void Create_WithOverlappingZonesAcrossKinds_ReturnsOverlappingZonesError()
    {
        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(
            4,
            [CreateSupportZone("support-a", 95m, 100m, 105m)],
            [CreateResistanceZone("resistance-a", 104m, 110m, 115m)]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("chart_analysis.overlapping_zones"));
    }

    [Test]
    public void Create_WithValidZones_ReturnsDefensiveCopies()
    {
        ChartZone[] supportZones = [CreateSupportZone("support-a", 95m, 100m, 105m)];

        Result<ChartAnalysisDefinition> result = ChartAnalysisDefinition.Create(4, supportZones, []);
        supportZones[0] = CreateSupportZone("support-b", 110m, 115m, 120m);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.SupportZones[0].Id.Value, Is.EqualTo("support-a"));
    }

    private static ChartZone CreateSupportZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id).Value,
            lower,
            level,
            upper,
            [
                ChartCondition.Create(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                ChartCondition.Create(ChartConditionType.SupportLoss, ChartAnalysisIdentifier.From("publish-signal").Value).Value
            ]).Value;
    }

    private static ChartZone CreateResistanceZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id).Value,
            lower,
            level,
            upper,
            [ChartCondition.Create(ChartConditionType.Breakout, ChartAnalysisIdentifier.From("publish-signal").Value).Value]).Value;
    }
}
