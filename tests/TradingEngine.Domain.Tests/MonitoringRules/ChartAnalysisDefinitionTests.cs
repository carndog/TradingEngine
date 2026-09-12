using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class ChartAnalysisDefinitionTests
{
    [Test]
    public void Create_WithValidZones_ReturnsDefinition()
    {
        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(
            4,
            [CreateSupportZone("support-a", 95m, 100m, 105m)],
            [CreateResistanceZone("resistance-a", 120m, 125m, 130m)]);

        Assert.Multiple(() =>
        {
            Assert.That(definition.PriceScale, Is.EqualTo(4));
            Assert.That(definition.SupportZones, Has.Count.EqualTo(1));
            Assert.That(definition.ResistanceZones, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Create_WithNoZones_ThrowsDomainRuleViolationException()
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => ChartAnalysisDefinition.Create(4, [], []));

        Assert.That(exception!.Rule, Is.EqualTo(ChartAnalysisDefinitionRule.MissingZones));
    }

    [Test]
    public void Create_WithPriceScaleAboveMaximum_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChartAnalysisDefinition.Create(
                9,
                [CreateSupportZone("support-a", 95m, 100m, 105m)],
                []));
    }

    [Test]
    public void Create_WithSupportZoneMissingSupportLoss_ThrowsDomainRuleViolationException()
    {
        ChartZone zone = ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a"),
            95m,
            100m,
            105m,
            [new ChartCondition(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal"))]);

        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => ChartAnalysisDefinition.Create(4, [zone], []));

        Assert.That(exception!.Rule, Is.EqualTo(ChartAnalysisDefinitionRule.InvalidConditionOrder));
    }

    [Test]
    public void Create_WithBreakoutConditionOnSupportZone_ThrowsDomainRuleViolationException()
    {
        ChartZone zone = ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a"),
            95m,
            100m,
            105m,
            [
                new ChartCondition(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal")),
                new ChartCondition(ChartConditionType.Breakout, ChartAnalysisIdentifier.From("publish-signal"))
            ]);

        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => ChartAnalysisDefinition.Create(4, [zone], []));

        Assert.That(exception!.Rule, Is.EqualTo(ChartAnalysisDefinitionRule.InvalidConditionOrder));
    }

    [Test]
    public void Create_WithDuplicateZoneIds_ThrowsDomainRuleViolationException()
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => ChartAnalysisDefinition.Create(
                4,
                [CreateSupportZone("shared-id", 95m, 100m, 105m)],
                [CreateResistanceZone("shared-id", 120m, 125m, 130m)]));

        Assert.That(exception!.Rule, Is.EqualTo(ChartAnalysisDefinitionRule.DuplicateZoneId));
    }

    [Test]
    public void Create_WithPriceExceedingScale_ThrowsDomainRuleViolationException()
    {
        ChartZone zone = ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a"),
            95.00001m,
            100m,
            105m,
            [
                new ChartCondition(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal")),
                new ChartCondition(ChartConditionType.SupportLoss, ChartAnalysisIdentifier.From("publish-signal"))
            ]);

        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => ChartAnalysisDefinition.Create(4, [zone], []));

        Assert.That(exception!.Rule, Is.EqualTo(ChartAnalysisDefinitionRule.PriceExceedsScale));
    }

    [Test]
    public void Create_WithTouchingZones_ThrowsDomainRuleViolationException()
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => ChartAnalysisDefinition.Create(
                4,
                [
                    CreateSupportZone("support-a", 95m, 100m, 105m),
                    CreateSupportZone("support-b", 105m, 110m, 115m)
                ],
                []));

        Assert.That(exception!.Rule, Is.EqualTo(ChartAnalysisDefinitionRule.OverlappingZones));
    }

    [Test]
    public void Create_WithOverlappingZonesAcrossKinds_ThrowsDomainRuleViolationException()
    {
        DomainRuleViolationException? exception = Assert.Throws<DomainRuleViolationException>(
            () => ChartAnalysisDefinition.Create(
                4,
                [CreateSupportZone("support-a", 95m, 100m, 105m)],
                [CreateResistanceZone("resistance-a", 104m, 110m, 115m)]));

        Assert.That(exception!.Rule, Is.EqualTo(ChartAnalysisDefinitionRule.OverlappingZones));
    }

    private static ChartZone CreateSupportZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id),
            lower,
            level,
            upper,
            [
                new ChartCondition(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal")),
                new ChartCondition(ChartConditionType.SupportLoss, ChartAnalysisIdentifier.From("publish-signal"))
            ]);
    }

    private static ChartZone CreateResistanceZone(string id, decimal lower, decimal level, decimal upper)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From(id),
            lower,
            level,
            upper,
            [new ChartCondition(ChartConditionType.Breakout, ChartAnalysisIdentifier.From("publish-signal"))]);
    }
}
