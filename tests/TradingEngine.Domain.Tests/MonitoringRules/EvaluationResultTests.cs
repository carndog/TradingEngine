using NodaTime;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class EvaluationResultTests
{
    private static readonly Instant ObservedAt = Instant.FromUtc(2026, 11, 1, 10, 0);

    [Test]
    public void Matches_WhenSourceListMutatedAfterConstruction_KeepsOriginalMatches()
    {
        List<TriggeredCondition> source = [Match("support-a", 95m, 100m, 105m)];
        EvaluationResult result = Result(source);

        source.Add(Match("support-b", 60m, 70m, 80m));
        source[0] = Match("support-c", 10m, 20m, 30m);

        Assert.Multiple(() =>
        {
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].ZoneId.Value, Is.EqualTo("support-a"));
        });
    }

    [Test]
    public void Matches_WhenMutatedThroughListInterface_ThrowsNotSupportedAndKeepsMatch()
    {
        EvaluationResult result = Result([Match("support-a", 95m, 100m, 105m)]);
        IList<TriggeredCondition> exposed = (IList<TriggeredCondition>)result.Matches;
        TriggeredCondition replacement = Match("support-b", 60m, 70m, 80m);

        Assert.Multiple(() =>
        {
            Assert.That(() => exposed[0] = replacement, Throws.TypeOf<NotSupportedException>());
            Assert.That(() => exposed.Add(replacement), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => exposed.Clear(), Throws.TypeOf<NotSupportedException>());
            Assert.That(result.Matches, Has.Count.EqualTo(1));
            Assert.That(result.Matches[0].ZoneId.Value, Is.EqualTo("support-a"));
        });
    }

    private static EvaluationResult Result(IReadOnlyList<TriggeredCondition> matches)
    {
        return new EvaluationResult(
            Guid.Parse("b2000000-0000-0000-0000-000000000001"),
            ObservedAt,
            null,
            EvaluationOutcome.Triggered,
            EvaluationReason.ConditionsTriggered,
            PriceBasis.Bid,
            "USD",
            104m,
            105m,
            matches);
    }

    private static TriggeredCondition Match(string zoneId, decimal lower, decimal level, decimal upper)
    {
        return new TriggeredCondition(
            ChartAnalysisIdentifier.From(zoneId).Value,
            ChartConditionType.SupportLoss,
            ChartAnalysisIdentifier.From("publish-signal").Value,
            lower,
            level,
            upper,
            96m,
            94m);
    }
}
