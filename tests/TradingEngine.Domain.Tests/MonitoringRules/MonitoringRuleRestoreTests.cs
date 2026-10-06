using NodaTime;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class MonitoringRuleRestoreTests
{
    private static readonly Guid RuleId = Guid.Parse("4d1a8d3e-5b6c-4f70-9a21-0f3e5c7b9d10");
    private static readonly Guid InstrumentId = Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0");
    private static readonly Guid FirstRevisionId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondRevisionId = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid DraftRevisionId = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    private static readonly Instant October1 = Instant.FromUtc(2026, 10, 1, 0, 0);
    private static readonly Instant October5 = Instant.FromUtc(2026, 10, 5, 0, 0);
    private static readonly Instant October6At10 = Instant.FromUtc(2026, 10, 6, 10, 0);
    private static readonly Instant October10 = Instant.FromUtc(2026, 10, 10, 0, 0);
    private const string Author = "synthetic-user";

    [Test]
    public void Restore_WithValidState_RebuildsRuleTimelineAndLookups()
    {
        Result<MonitoringRule> result = MonitoringRule.Restore(
            RuleId,
            InstrumentId,
            October1,
            [
                Committed(FirstRevisionId, 1, 100m, October1, October1, October6At10, "initial"),
                Committed(SecondRevisionId, 2, 110m, October5, October6At10, null, "raise support"),
                Draft(DraftRevisionId, 120m, October5, "candidate", October10, null)
            ]);

        Assert.That(result.IsSuccess, Is.True);
        MonitoringRule rule = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(rule.Id, Is.EqualTo(RuleId));
            Assert.That(rule.WatchedInstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(rule.CreatedAt, Is.EqualTo(October1));
            Assert.That(rule.Revisions, Has.Count.EqualTo(2));
            Assert.That(rule.Drafts, Has.Count.EqualTo(1));
            Assert.That(rule.EffectiveAt(October1)!.Id, Is.EqualTo(FirstRevisionId));
            Assert.That(
                rule.EffectiveAt(October6At10 - Duration.FromTicks(1))!.Id,
                Is.EqualTo(FirstRevisionId));
            Assert.That(rule.EffectiveAt(October6At10)!.Id, Is.EqualTo(SecondRevisionId));
            Assert.That(rule.EffectiveAt(October10)!.Id, Is.EqualTo(SecondRevisionId));
            Assert.That(rule.EffectiveAt(October1 - Duration.FromTicks(1)), Is.Null);
            Assert.That(rule.FindRevision(DraftRevisionId)!.IsDraft, Is.True);
            Assert.That(
                rule.FindRevision(DraftRevisionId)!.Proposal,
                Is.EqualTo(new RevisionProposal(October10, null)));
        });
    }

    [Test]
    public void Restore_WithEmptyId_ReturnsIdRequiredError()
    {
        Result<MonitoringRule> result = MonitoringRule.Restore(
            Guid.Empty,
            InstrumentId,
            October1,
            []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(MonitoringRuleErrors.IdRequired));
    }

    [Test]
    public void Restore_WithEmptyWatchedInstrumentId_ReturnsWatchedInstrumentIdRequiredError()
    {
        Result<MonitoringRule> result = MonitoringRule.Restore(
            RuleId,
            Guid.Empty,
            October1,
            []);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(MonitoringRuleErrors.WatchedInstrumentIdRequired));
    }

    [Test]
    public void Restore_WithCorruptTimeline_PropagatesTimelineError()
    {
        Result<MonitoringRule> result = MonitoringRule.Restore(
            RuleId,
            InstrumentId,
            October1,
            [
                Committed(FirstRevisionId, 1, 100m, October1, October1, October5, null),
                Committed(SecondRevisionId, 2, 110m, October5, October6At10, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredSequenceInvalid));
    }

    [Test]
    public void Restore_ThenMutations_EnforceTemporalRulesAsUsual()
    {
        MonitoringRule rule = MonitoringRule.Restore(
            RuleId,
            InstrumentId,
            October1,
            [
                Committed(FirstRevisionId, 1, 100m, October1, October1, null, null)
            ]).Value;

        Result<Revision<ChartAnalysisDefinition>> draft = rule.CreateDraft(
            SecondRevisionId,
            CreateDefinition(110m),
            October5,
            Author,
            "raise support",
            null);
        Result scheduled = rule.Schedule(SecondRevisionId, October6At10, null, Guid.NewGuid(), October5);
        Result rescheduled = rule.Reschedule(SecondRevisionId, October10, October5);
        Result removed = rule.RemoveScheduledRevision(SecondRevisionId, October5);

        Assert.Multiple(() =>
        {
            Assert.That(draft.IsSuccess, Is.True);
            Assert.That(scheduled.IsSuccess, Is.True);
            Assert.That(rescheduled.IsSuccess, Is.True);
            Assert.That(removed.IsSuccess, Is.True);
            Assert.That(rule.FindRevision(FirstRevisionId)!.EffectivePeriod!.IsOpenEnded, Is.True);
            Assert.That(rule.Revisions, Has.Count.EqualTo(1));
        });
    }

    private static RestoredRevision<ChartAnalysisDefinition> Committed(
        Guid id,
        int? number,
        decimal level,
        Instant createdAt,
        Instant effectiveFrom,
        Instant? effectiveTo,
        string? changeReason)
    {
        return new RestoredRevision<ChartAnalysisDefinition>(
            id,
            CreateDefinition(level),
            createdAt,
            Author,
            changeReason,
            number,
            effectiveFrom,
            effectiveTo,
            null,
            null);
    }

    private static RestoredRevision<ChartAnalysisDefinition> Draft(
        Guid id,
        decimal level,
        Instant createdAt,
        string? changeReason,
        Instant? proposedFrom,
        Instant? proposedTo)
    {
        return new RestoredRevision<ChartAnalysisDefinition>(
            id,
            CreateDefinition(level),
            createdAt,
            Author,
            changeReason,
            null,
            null,
            null,
            proposedFrom,
            proposedTo);
    }

    private static ChartAnalysisDefinition CreateDefinition(decimal supportLevel)
    {
        return ChartAnalysisDefinition.Create(
            4,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    supportLevel - 5m,
                    supportLevel,
                    supportLevel + 5m,
                    [
                        ChartCondition.Create(
                            ChartConditionType.BuyZone,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                        ChartCondition.Create(
                            ChartConditionType.SupportLoss,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value
                    ]).Value
            ],
            []).Value;
    }
}
