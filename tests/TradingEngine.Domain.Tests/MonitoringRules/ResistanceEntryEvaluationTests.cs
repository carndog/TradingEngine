using NodaTime;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class ResistanceEntryEvaluationTests
{
    private static readonly Instant Start = Instant.FromUtc(2026, 1, 1, 0, 0);
    private static readonly Guid RevisionId = Guid.Parse("b2000000-0000-0000-0000-000000000003");

    [TestCase(119, 120, ChartConditionType.ResistanceEntry)]
    [TestCase(119, 125, ChartConditionType.ResistanceEntry)]
    [TestCase(119, 130, ChartConditionType.ResistanceEntry)]
    [TestCase(119, 131, ChartConditionType.Breakout)]
    [TestCase(130, 131, ChartConditionType.Breakout)]
    public void Evaluate_UpwardCrossing_ReturnsOnlyObservedCondition(
        decimal previous, decimal current, ChartConditionType expected)
    {
        ChartAnalysisDefinition definition = Definition(Zone("resistance-a", 120m, 125m, 130m));

        EvaluationResult result = Evaluate(definition, previous, current);

        Assert.Multiple(() =>
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].Condition, Is.EqualTo(expected));
            Assert.That(result.Matches[0].Lower, Is.EqualTo(120m));
            Assert.That(result.Matches[0].Level, Is.EqualTo(125m));
            Assert.That(result.Matches[0].Upper, Is.EqualTo(130m));
            Assert.That(result.Matches[0].PreviousPrice, Is.EqualTo(previous));
            Assert.That(result.Matches[0].CurrentPrice, Is.EqualTo(current));
            Assert.That(result.Matches[0].ActionId.Value, Is.EqualTo("publish-signal"));
            Assert.That(result.MonitoringRuleRevisionId, Is.EqualTo(RevisionId));
        });
    }

    [TestCase(120, 125)]
    [TestCase(125, 130)]
    [TestCase(125, 125)]
    [TestCase(131, 125)]
    [TestCase(131, 130)]
    [TestCase(125, 119)]
    [TestCase(118, 119)]
    public void Evaluate_WithoutEntryFromBelow_ReturnsNoAction(decimal previous, decimal current)
    {
        ChartAnalysisDefinition definition = Definition(Zone("resistance-a", 120m, 125m, 130m));

        EvaluationResult result = Evaluate(definition, previous, current);

        Assert.That(result.Outcome, Is.EqualTo(EvaluationOutcome.NoAction));
        Assert.That(result.Matches, Is.Empty);
    }

    [Test]
    public void Evaluate_FirstObservationInsideResistance_ReturnsNoAction()
    {
        ChartAnalysisDefinition definition = Definition(Zone("resistance-a", 120m, 125m, 130m));

        EvaluationResult result = Evaluate(definition, null, 125m);

        Assert.That(result.Reason, Is.EqualTo(EvaluationReason.FirstObservation));
        Assert.That(result.Matches, Is.Empty);
    }

    [Test]
    public void Evaluate_BreakoutOnlyDefinition_DoesNotInventEntry()
    {
        ChartAnalysisDefinition definition = Definition(Zone("resistance-a", 120m, 125m, 130m, false));

        EvaluationResult result = Evaluate(definition, 119m, 125m);

        Assert.That(result.Matches, Is.Empty);
        Assert.That(definition.ResistanceZones[0].Conditions, Has.Count.EqualTo(1));
    }

    [Test]
    public void Evaluate_NoResistanceZones_DoesNotInventEntry()
    {
        EvaluationResult result = Evaluate(ChartAnalysisDefinition.Create(2, [], []).Value, 119m, 125m);

        Assert.That(result.Reason, Is.EqualTo(EvaluationReason.NoConfiguredZones));
        Assert.That(result.Matches, Is.Empty);
    }

    [TestCase(PriceBasis.Bid)]
    [TestCase(PriceBasis.Ask)]
    public void Evaluate_FractionalBoundary_UsesExactSelectedSide(PriceBasis basis)
    {
        ChartAnalysisDefinition definition = Definition(Zone("resistance-a", 120.01m, 125m, 130m));
        PriceObservation previous = Observation(Start, 120m);
        PriceObservation current = PriceObservation.Create(Start + Duration.FromMinutes(1),
            basis == PriceBasis.Bid ? 120.01m : 120m,
            basis == PriceBasis.Ask ? 120.01m : 120m, "USD").Value;

        EvaluationResult result = new ChartAnalysisEvaluator().Evaluate(
            new EvaluationInput(RevisionId, definition, previous, current, basis)).Value;

        Assert.That(result.PriceBasis, Is.EqualTo(basis));
        Assert.That(result.Matches.Single().Condition, Is.EqualTo(ChartConditionType.ResistanceEntry));
        Assert.That(result.Matches.Single().CurrentPrice, Is.EqualTo(120.01m));
    }

    [Test]
    public void Evaluate_ReentryAfterObservedFallBelow_TriggersAgain()
    {
        ChartAnalysisDefinition definition = Definition(Zone("resistance-a", 120m, 125m, 130m));
        decimal[] prices = [119m, 125m, 126m, 119m, 120m];
        List<ChartConditionType> matches = [];

        for (int index = 1; index < prices.Length; index++)
        {
            PriceObservation previous = Observation(Start + Duration.FromMinutes(index - 1), prices[index - 1]);
            PriceObservation current = Observation(Start + Duration.FromMinutes(index), prices[index]);
            EvaluationResult result = new ChartAnalysisEvaluator().Evaluate(
                new EvaluationInput(RevisionId, definition, previous, current, PriceBasis.Bid)).Value;
            matches.AddRange(result.Matches.Select(match => match.Condition));
        }

        Assert.That(matches, Is.EqualTo(new[] { ChartConditionType.ResistanceEntry, ChartConditionType.ResistanceEntry }));
    }

    [Test]
    public void Evaluate_JumpsLowerZoneAndEntersHigherZone_ReturnsOrderedDistinctEvents()
    {
        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(2, [],
            [Zone("higher", 140m, 145m, 150m), Zone("lower", 120m, 125m, 130m)]).Value;

        EvaluationResult result = Evaluate(definition, 119m, 145m);

        Assert.That(result.Matches.Select(match => (match.ZoneId.Value, match.Condition)), Is.EqualTo(new[]
        {
            ("lower", ChartConditionType.Breakout),
            ("higher", ChartConditionType.ResistanceEntry)
        }));
    }

    private static EvaluationResult Evaluate(ChartAnalysisDefinition definition, decimal? previous, decimal current)
    {
        PriceObservation? previousObservation = previous.HasValue ? Observation(Start, previous.Value) : null;
        return new ChartAnalysisEvaluator().Evaluate(new EvaluationInput(RevisionId, definition,
            previousObservation, Observation(Start + Duration.FromMinutes(1), current), PriceBasis.Bid)).Value;
    }

    private static PriceObservation Observation(Instant time, decimal price)
    {
        return PriceObservation.Create(time, price, price, "USD").Value;
    }

    private static ChartAnalysisDefinition Definition(ChartZone zone)
    {
        return ChartAnalysisDefinition.Create(2, [], [zone]).Value;
    }

    private static ChartZone Zone(string id, decimal lower, decimal level, decimal upper, bool entry = true)
    {
        ChartAnalysisIdentifier action = ChartAnalysisIdentifier.From("publish-signal").Value;
        List<ChartCondition> conditions = [ChartCondition.Create(ChartConditionType.Breakout, action).Value];
        if (entry)
        {
            conditions.Add(ChartCondition.Create(ChartConditionType.ResistanceEntry, action).Value);
        }

        return ChartZone.Create(ChartAnalysisIdentifier.From(id).Value, lower, level, upper, conditions).Value;
    }
}
