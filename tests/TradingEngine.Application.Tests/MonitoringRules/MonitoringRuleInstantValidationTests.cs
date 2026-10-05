using NodaTime;
using TradingEngine.Application.Time;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Application.Tests.MonitoringRules;

[TestFixture]
public sealed class MonitoringRuleInstantValidationTests
{
    private static readonly Guid RuleId = Guid.Parse("c3000000-0000-0000-0000-000000000001");
    private static readonly Guid InstrumentId = Guid.Parse("c3000000-0000-0000-0000-000000000002");
    private static readonly Guid RevisionA = Guid.Parse("c3000000-0000-0000-0000-000000000011");
    private static readonly Guid RevisionB = Guid.Parse("c3000000-0000-0000-0000-000000000012");
    private static readonly Guid RevisionC = Guid.Parse("c3000000-0000-0000-0000-000000000013");
    private static readonly Guid ContinuationId = Guid.Parse("c3000000-0000-0000-0000-000000000090");
    private static readonly Instant October1 = Instant.FromUtc(2026, 10, 1, 0, 0);
    private static readonly Instant October10 = Instant.FromUtc(2026, 10, 10, 0, 0);
    private static readonly Instant October15 = Instant.FromUtc(2026, 10, 15, 0, 0);
    private static readonly Instant October20 = Instant.FromUtc(2026, 10, 20, 0, 0);
    private const string Author = "synthetic-user";

    [Test]
    public void Reschedule_WithSupportedBoundary_MutatesTimeline()
    {
        MonitoringRule rule = CreateRuleWithFutureRevision();

        Result rescheduled = RescheduleValidated(rule, RevisionB, October20, October1);

        Assert.Multiple(() =>
        {
            Assert.That(rescheduled.IsSuccess, Is.True);
            Assert.That(
                rule.FindRevision(RevisionB)!.EffectivePeriod!.EffectiveFrom,
                Is.EqualTo(October20));
            Assert.That(
                rule.FindRevision(RevisionA)!.EffectivePeriod!.EffectiveTo,
                Is.EqualTo(October20));
        });
    }

    [Test]
    public void Reschedule_WithUnsupportedBoundary_ThrowsBeforeMutatingTimeline()
    {
        MonitoringRule rule = CreateRuleWithFutureRevision();
        Instant subTick = October20.PlusNanoseconds(50);

        Assert.That(
            () => RescheduleValidated(rule, RevisionB, subTick, October1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.Multiple(() =>
        {
            Assert.That(
                rule.FindRevision(RevisionB)!.EffectivePeriod!.EffectiveFrom,
                Is.EqualTo(October10));
            Assert.That(
                rule.FindRevision(RevisionA)!.EffectivePeriod!.EffectiveTo,
                Is.EqualTo(October10));
        });
    }

    [Test]
    public void Schedule_WithSubTickComparisonInstant_KeepsExactComparisonSemantics()
    {
        MonitoringRule rule = CreateRule();
        rule.CreateDraft(RevisionB, CreateDefinition(110m), October1, Author, "b", null);
        Instant subTickNow = October1.PlusNanoseconds(1);

        Result scheduled = ScheduleValidated(rule, RevisionB, October10, subTickNow);

        Assert.Multiple(() =>
        {
            Assert.That(scheduled.IsSuccess, Is.True);
            Assert.That(
                rule.FindRevision(RevisionB)!.EffectivePeriod!.EffectiveFrom,
                Is.EqualTo(October10));
            Assert.That(
                rule.FindRevision(RevisionA)!.EffectivePeriod!.EffectiveTo,
                Is.EqualTo(October10));
        });
    }

    [Test]
    public void Schedule_WithUnsupportedBoundary_ThrowsBeforeMutatingTimeline()
    {
        MonitoringRule rule = CreateRule();
        rule.CreateDraft(RevisionB, CreateDefinition(110m), October1, Author, "b", null);

        Assert.That(
            () => ScheduleValidated(rule, RevisionB, October10.PlusNanoseconds(1), October1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.Multiple(() =>
        {
            Assert.That(rule.FindRevision(RevisionB)!.IsDraft, Is.True);
            Assert.That(
                rule.FindRevision(RevisionA)!.EffectivePeriod!.IsOpenEnded,
                Is.True);
        });
    }

    [Test]
    public void ApplyNow_WithUnsupportedNow_ThrowsBeforeCommittingDraft()
    {
        MonitoringRule rule = CreateRule();
        rule.CreateDraft(RevisionB, CreateDefinition(110m), October1, Author, "b", null);

        Assert.That(
            () => ApplyNowValidated(rule, RevisionB, October1.PlusNanoseconds(50)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(rule.FindRevision(RevisionB)!.IsDraft, Is.True);
    }

    [Test]
    public void CreateDraft_WithSupportedProposals_PersistsDraftOutsideCommittedRules()
    {
        MonitoringRule rule = CreateRule();
        RevisionProposal inverted = new(October15, October10);
        RevisionProposal open = new(October20, null);

        Result<Revision<ChartAnalysisDefinition>> first = CreateDraftValidated(
            rule,
            RevisionB,
            CreateDefinition(100m),
            October1,
            inverted);
        Result<Revision<ChartAnalysisDefinition>> second = CreateDraftValidated(
            rule,
            RevisionC,
            CreateDefinition(110m),
            October1,
            open);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(rule.FindRevision(RevisionB)!.Proposal, Is.EqualTo(inverted));
            Assert.That(rule.FindRevision(RevisionC)!.Proposal, Is.EqualTo(open));
            Assert.That(rule.Revisions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void CreateDraft_WithUnsupportedProposalDate_ThrowsBeforeAddingDraft()
    {
        MonitoringRule rule = CreateRule();
        RevisionProposal subTick = new(October10.PlusNanoseconds(50), null);

        Assert.That(
            () => CreateDraftValidated(
                rule,
                RevisionA,
                CreateDefinition(100m),
                October1,
                subTick),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.Multiple(() =>
        {
            Assert.That(rule.Drafts, Is.Empty);
        });
    }

    [Test]
    public void Create_WithUnsupportedCreatedAt_ThrowsBeforeCreatingRule()
    {
        Assert.That(
            () => CreateRuleValidated(
                RuleId,
                InstrumentId,
                October1.PlusNanoseconds(50)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static Result RescheduleValidated(
        MonitoringRule rule,
        Guid revisionId,
        Instant effectiveFrom,
        Instant now)
    {
        Instant boundary = PersistedInstant.Require(effectiveFrom, nameof(effectiveFrom));

        return rule.Reschedule(revisionId, boundary, now);
    }

    private static Result ScheduleValidated(
        MonitoringRule rule,
        Guid draftId,
        Instant effectiveFrom,
        Instant now)
    {
        Instant boundary = PersistedInstant.Require(effectiveFrom, nameof(effectiveFrom));

        return rule.Schedule(draftId, boundary, null, ContinuationId, now);
    }

    private static Result ApplyNowValidated(
        MonitoringRule rule,
        Guid draftId,
        Instant now)
    {
        Instant committed = PersistedInstant.Require(now, nameof(now));

        return rule.ApplyNow(draftId, ContinuationId, committed);
    }

    private static Result<Revision<ChartAnalysisDefinition>> CreateDraftValidated(
        MonitoringRule rule,
        Guid draftId,
        ChartAnalysisDefinition definition,
        Instant createdAt,
        RevisionProposal? proposal)
    {
        Instant created = PersistedInstant.Require(createdAt, nameof(createdAt));
        Instant? proposedFrom = PersistedInstant.Require(
            proposal?.EffectiveFrom,
            nameof(proposal));
        Instant? proposedTo = PersistedInstant.Require(
            proposal?.EffectiveTo,
            nameof(proposal));

        return rule.CreateDraft(
            draftId,
            definition,
            created,
            Author,
            null,
            new RevisionProposal(proposedFrom, proposedTo));
    }

    private static Result<MonitoringRule> CreateRuleValidated(
        Guid id,
        Guid watchedInstrumentId,
        Instant createdAt)
    {
        Instant created = PersistedInstant.Require(createdAt, nameof(createdAt));

        return MonitoringRule.Create(
            id,
            watchedInstrumentId,
            RevisionA,
            CreateDefinition(100m),
            created,
            Author);
    }

    private static MonitoringRule CreateRule()
    {
        return CreateRuleValidated(RuleId, InstrumentId, October1).Value;
    }

    private static MonitoringRule CreateRuleWithFutureRevision()
    {
        MonitoringRule rule = CreateRule();
        rule.CreateDraft(RevisionB, CreateDefinition(110m), October1, Author, "b", null);
        rule.Schedule(RevisionB, October10, null, ContinuationId, October1);

        return rule;
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
