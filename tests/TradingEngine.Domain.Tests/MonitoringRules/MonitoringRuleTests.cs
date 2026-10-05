using NodaTime;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Domain.Tests.MonitoringRules;

[TestFixture]
public sealed class MonitoringRuleTests
{
    private static readonly Guid RuleId = Guid.Parse("4d1a8d3e-5b6c-4f70-9a21-0f3e5c7b9d10");
    private static readonly Guid InstrumentId = Guid.Parse("1788c81b-2d9b-4686-ab26-b62685d7bda0");
    private static readonly Guid FirstRevisionId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondRevisionId = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdRevisionId = Guid.Parse("a1000000-0000-0000-0000-000000000003");
    private static readonly Guid FourthRevisionId = Guid.Parse("a1000000-0000-0000-0000-000000000004");
    private static readonly Guid FifthRevisionId = Guid.Parse("a1000000-0000-0000-0000-000000000005");
    private static readonly Guid ContinuationId = Guid.Parse("a1000000-0000-0000-0000-000000000090");
    private static readonly Instant October1 = Instant.FromUtc(2026, 10, 1, 0, 0);
    private static readonly Instant October5 = Instant.FromUtc(2026, 10, 5, 0, 0);
    private static readonly Instant October6At10 = Instant.FromUtc(2026, 10, 6, 10, 0);
    private static readonly Instant October10 = Instant.FromUtc(2026, 10, 10, 0, 0);
    private const string Author = "synthetic-user";

    [Test]
    public void Create_WithValidIdentity_CommitsInitialRevisionFromCreatedAt()
    {
        ChartAnalysisDefinition definition = CreateDefinition(100m);

        Result<MonitoringRule> result = MonitoringRule.Create(
            RuleId,
            InstrumentId,
            FirstRevisionId,
            definition,
            October1,
            Author);

        Assert.That(result.IsSuccess, Is.True);
        Revision<ChartAnalysisDefinition> initial = result.Value.Revisions[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Id, Is.EqualTo(RuleId));
            Assert.That(result.Value.WatchedInstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(result.Value.CreatedAt, Is.EqualTo(October1));
            Assert.That(result.Value.CoverageOrigin, Is.EqualTo(October1));
            Assert.That(result.Value.Revisions, Has.Count.EqualTo(1));
            Assert.That(result.Value.Drafts, Is.Empty);
            Assert.That(initial.Id, Is.EqualTo(FirstRevisionId));
            Assert.That(initial.Definition, Is.SameAs(definition));
            Assert.That(initial.CreatedBy, Is.EqualTo(Author));
            Assert.That(initial.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October1));
            Assert.That(initial.EffectivePeriod.EffectiveTo, Is.Null);
            Assert.That(initial.RevisionNumber, Is.EqualTo(1));
            Assert.That(result.Value.EffectiveAt(October1), Is.SameAs(initial));
        });
    }

    [Test]
    public void Create_WithEmptyId_ReturnsIdRequiredError()
    {
        Result<MonitoringRule> result = MonitoringRule.Create(
            Guid.Empty,
            InstrumentId,
            FirstRevisionId,
            CreateDefinition(100m),
            October1,
            Author);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(MonitoringRuleErrors.IdRequired));
    }

    [Test]
    public void Create_WithEmptyWatchedInstrumentId_ReturnsWatchedInstrumentIdRequiredError()
    {
        Result<MonitoringRule> result = MonitoringRule.Create(
            RuleId,
            Guid.Empty,
            FirstRevisionId,
            CreateDefinition(100m),
            October1,
            Author);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(MonitoringRuleErrors.WatchedInstrumentIdRequired));
    }

    [Test]
    public void ApplyNow_WithCurrentRevision_CommitsDraftOpenEndedFromNow()
    {
        MonitoringRule rule = CreateRule();
        ChartAnalysisDefinition definition = CreateDefinition(110m);
        Revision<ChartAnalysisDefinition> draft = rule
            .CreateDraft(SecondRevisionId, definition, October5, Author, "raise support", null)
            .Value;

        Result result = rule.ApplyNow(draft.Id, FourthRevisionId, October5);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rule.EffectiveAt(October5), Is.SameAs(draft));
            Assert.That(rule.EffectiveAt(October5)!.Definition, Is.SameAs(definition));
            Assert.That(draft.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October5));
            Assert.That(draft.EffectivePeriod.EffectiveTo, Is.Null);
            Assert.That(draft.RevisionNumber, Is.EqualTo(2));
            Assert.That(rule.Drafts, Is.Empty);
            Assert.That(rule.FindRevision(FirstRevisionId)!.EffectivePeriod!.EffectiveTo, Is.EqualTo(October5));
        });
    }

    [Test]
    public void CreateDraft_WithTwoOverlappingProposals_LeavesEffectiveTimelineUnchanged()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();
        RevisionProposal proposal = new(October6At10, null);

        Result<Revision<ChartAnalysisDefinition>> first = rule.CreateDraft(
            SecondRevisionId,
            CreateDefinition(110m),
            October5,
            Author,
            "alternative a",
            proposal);
        Result<Revision<ChartAnalysisDefinition>> second = rule.CreateDraft(
            ThirdRevisionId,
            CreateDefinition(120m),
            October5,
            Author,
            "alternative b",
            proposal);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(rule.Drafts, Has.Count.EqualTo(2));
            Assert.That(rule.Revisions, Has.Count.EqualTo(1));
            Assert.That(rule.EffectiveAt(October6At10)!.Id, Is.EqualTo(FirstRevisionId));
            Assert.That(rule.EffectiveAt(October6At10)!.Definition.SupportZones[0].Level, Is.EqualTo(100m));
        });
    }

    [Test]
    public void Schedule_OnOctober5ForOctober6_SplitsOpenEndedRevisionAsInIssueExample()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();
        ChartAnalysisDefinition changed = CreateDefinition(110m);
        Revision<ChartAnalysisDefinition> draft = rule
            .CreateDraft(SecondRevisionId, changed, October5, Author, "raise support", null)
            .Value;

        Result result = rule.Schedule(draft.Id, October6At10, null, ContinuationId, October5);

        Revision<ChartAnalysisDefinition> original = rule.FindRevision(FirstRevisionId)!;
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(original.Id, Is.EqualTo(FirstRevisionId));
            Assert.That(original.Definition.SupportZones[0].Level, Is.EqualTo(100m));
            Assert.That(original.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October1));
            Assert.That(original.EffectivePeriod.EffectiveTo, Is.EqualTo(October6At10));
            Assert.That(draft.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October6At10));
            Assert.That(draft.EffectivePeriod.EffectiveTo, Is.Null);
            Assert.That(rule.EffectiveAt(October6At10 - Duration.FromSeconds(1)), Is.SameAs(original));
            Assert.That(rule.EffectiveAt(October6At10), Is.SameAs(draft));
            Assert.That(rule.EffectiveAt(October6At10)!.Definition, Is.SameAs(changed));
        });
    }

    [Test]
    public void EditScheduledRevision_BeforeStart_ReplacesDefinition()
    {
        MonitoringRule rule = CreateRuleWithScheduledRevision();
        ChartAnalysisDefinition replacement = CreateDefinition(115m);

        Result result = rule.EditScheduledRevision(
            SecondRevisionId,
            replacement,
            "fine tune",
            October6At10 - Duration.FromSeconds(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(rule.FindRevision(SecondRevisionId)!.Definition, Is.SameAs(replacement));
    }

    [Test]
    public void EditScheduledRevision_AtExactStart_ReturnsPeriodBegunError()
    {
        MonitoringRule rule = CreateRuleWithScheduledRevision();
        ChartAnalysisDefinition before = rule.FindRevision(SecondRevisionId)!.Definition;

        Result result = rule.EditScheduledRevision(SecondRevisionId, CreateDefinition(115m), null, October6At10);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(rule.FindRevision(SecondRevisionId)!.Definition, Is.SameAs(before));
        });
    }

    [Test]
    public void EditScheduledRevision_ForCurrentRevision_ReturnsPeriodBegunError()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();

        Result result = rule.EditScheduledRevision(FirstRevisionId, CreateDefinition(115m), null, October5);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
    }

    [Test]
    public void ApplyNow_WithCurrentRevision_SplitsImmediatelyAndSuccessorIsImmutableFromStart()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();
        Revision<ChartAnalysisDefinition> draft = rule
            .CreateDraft(SecondRevisionId, CreateDefinition(110m), October5, Author, null, null)
            .Value;

        Result applied = rule.ApplyNow(draft.Id, ContinuationId, October5);
        Result edited = rule.EditScheduledRevision(draft.Id, CreateDefinition(120m), null, October5);

        Assert.Multiple(() =>
        {
            Assert.That(applied.IsSuccess, Is.True);
            Assert.That(edited.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(rule.FindRevision(FirstRevisionId)!.EffectivePeriod!.EffectiveTo, Is.EqualTo(October5));
            Assert.That(draft.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October5));
            Assert.That(draft.Definition.SupportZones[0].Level, Is.EqualTo(110m));
        });
    }

    [Test]
    public void Schedule_InsideExistingPeriod_PreservesLaterScheduledRevision()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();
        Revision<ChartAnalysisDefinition> later = rule
            .CreateDraft(SecondRevisionId, CreateDefinition(130m), October5, Author, null, null)
            .Value;
        rule.Schedule(later.Id, October10, null, FourthRevisionId, October5);
        Revision<ChartAnalysisDefinition> middle = rule
            .CreateDraft(ThirdRevisionId, CreateDefinition(110m), October5, Author, null, null)
            .Value;

        Result result = rule.Schedule(middle.Id, October6At10, October10, FifthRevisionId, October5);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rule.FindRevision(FirstRevisionId)!.EffectivePeriod!.EffectiveTo, Is.EqualTo(October6At10));
            Assert.That(middle.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October6At10));
            Assert.That(middle.EffectivePeriod.EffectiveTo, Is.EqualTo(October10));
            Assert.That(later.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October10));
            Assert.That(later.EffectivePeriod.EffectiveTo, Is.Null);
            Assert.That(rule.Revisions.Select(revision => revision.RevisionNumber), Is.EqualTo(new int?[] { 1, 2, 3 }));
            Assert.That(rule.Revisions.Select(revision => revision.Id), Is.EqualTo(new[] { FirstRevisionId, ThirdRevisionId, SecondRevisionId }));
        });
    }

    [Test]
    public void Reschedule_FutureRevision_MovesBoundaryWithoutChangingHistory()
    {
        MonitoringRule rule = CreateRuleWithScheduledRevision();

        Result result = rule.Reschedule(SecondRevisionId, October10, October5);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rule.FindRevision(FirstRevisionId)!.EffectivePeriod!.EffectiveTo, Is.EqualTo(October10));
            Assert.That(rule.FindRevision(SecondRevisionId)!.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October10));
            Assert.That(rule.EffectiveAt(October6At10)!.Id, Is.EqualTo(FirstRevisionId));
            Assert.That(rule.EffectiveAt(October1)!.Definition.SupportZones[0].Level, Is.EqualTo(100m));
        });
    }

    [Test]
    public void RemoveScheduledRevision_FutureRevision_RestoresOpenEndedCurrentRevision()
    {
        MonitoringRule rule = CreateRuleWithScheduledRevision();

        Result result = rule.RemoveScheduledRevision(SecondRevisionId, October5);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rule.FindRevision(SecondRevisionId), Is.Null);
            Assert.That(rule.FindRevision(FirstRevisionId)!.EffectivePeriod!.IsOpenEnded, Is.True);
            Assert.That(rule.EffectiveAt(October10)!.Id, Is.EqualTo(FirstRevisionId));
        });
    }

    [Test]
    public void RemoveScheduledRevision_CurrentRevision_ReturnsPeriodBegunError()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();

        Result result = rule.RemoveScheduledRevision(FirstRevisionId, October5);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(rule.Revisions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Schedule_BeforeNow_ReturnsBackdatedErrorAndLeavesDraftAndTimelineUnchanged()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();
        Revision<ChartAnalysisDefinition> draft = rule
            .CreateDraft(SecondRevisionId, CreateDefinition(110m), October5, Author, null, null)
            .Value;

        Result result = rule.Schedule(draft.Id, October5, null, ContinuationId, October6At10);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.Backdated));
            Assert.That(draft.IsDraft, Is.True);
            Assert.That(rule.Drafts, Is.EqualTo(new[] { draft }));
            Assert.That(rule.Revisions, Has.Count.EqualTo(1));
            Assert.That(rule.FindRevision(FirstRevisionId)!.EffectivePeriod!.IsOpenEnded, Is.True);
        });
    }

    [Test]
    public void DeleteDraft_WithExistingDraft_LeavesEffectiveTimelineUnchanged()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();
        Revision<ChartAnalysisDefinition> draft = rule
            .CreateDraft(SecondRevisionId, CreateDefinition(110m), October5, Author, null, new RevisionProposal(October1, null))
            .Value;

        Result result = rule.DeleteDraft(draft.Id);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rule.Drafts, Is.Empty);
            Assert.That(rule.EffectiveAt(October5)!.Id, Is.EqualTo(FirstRevisionId));
        });
    }

    [Test]
    public void EffectiveAt_AcrossHistoryAfterMultipleChanges_ReturnsOriginalDefinitions()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();
        ChartAnalysisDefinition second = CreateDefinition(110m);
        ChartAnalysisDefinition third = CreateDefinition(120m);
        rule.CreateDraft(SecondRevisionId, second, October5, Author, null, null);
        rule.ApplyNow(SecondRevisionId, FourthRevisionId, October5);
        rule.CreateDraft(ThirdRevisionId, third, October6At10, Author, null, null);
        rule.ApplyNow(ThirdRevisionId, FifthRevisionId, October6At10);

        Assert.Multiple(() =>
        {
            Assert.That(rule.EffectiveAt(October1)!.Definition.SupportZones[0].Level, Is.EqualTo(100m));
            Assert.That(rule.EffectiveAt(October5)!.Definition, Is.SameAs(second));
            Assert.That(rule.EffectiveAt(October6At10)!.Definition, Is.SameAs(third));
            Assert.That(rule.EffectiveAt(October10)!.Definition, Is.SameAs(third));
            Assert.That(rule.EffectiveAt(October1 - Duration.FromSeconds(1)), Is.Null);
        });
    }

    [Test]
    public void EffectiveAt_AfterMutationAttemptsOnSupersededDefinition_ReturnsUnchangedHistoricalRevision()
    {
        ChartAnalysisDefinition callerHeld = CreateDefinition(100m);
        MonitoringRule rule = MonitoringRule.Create(
            RuleId,
            InstrumentId,
            FirstRevisionId,
            callerHeld,
            October1,
            Author).Value;
        rule.CreateDraft(SecondRevisionId, CreateDefinition(110m), October5, Author, null, null);
        rule.ApplyNow(SecondRevisionId, ThirdRevisionId, October5);
        ChartZone replacementZone = CreateDefinition(300m).SupportZones[0];
        ChartCondition replacementCondition = CreateDefinition(100m).ResistanceZones[0].Conditions[0];
        IList<ChartZone> callerHeldZones = (IList<ChartZone>)callerHeld.SupportZones;
        IList<ChartZone> exposedZones = (IList<ChartZone>)rule.EffectiveAt(October1)!.Definition.SupportZones;
        IList<ChartZone> exposedResistance = (IList<ChartZone>)rule.EffectiveAt(October1)!.Definition.ResistanceZones;
        IList<ChartCondition> exposedConditions = (IList<ChartCondition>)rule.EffectiveAt(October1)!.Definition.SupportZones[0].Conditions;

        Assert.That(() => callerHeldZones[0] = replacementZone, Throws.TypeOf<NotSupportedException>());
        Assert.That(() => exposedZones[0] = replacementZone, Throws.TypeOf<NotSupportedException>());
        Assert.That(() => exposedResistance[0] = replacementZone, Throws.TypeOf<NotSupportedException>());
        Assert.That(() => exposedConditions[0] = replacementCondition, Throws.TypeOf<NotSupportedException>());

        Revision<ChartAnalysisDefinition> historical = rule.EffectiveAt(October1)!;
        Assert.Multiple(() =>
        {
            Assert.That(historical.Id, Is.EqualTo(FirstRevisionId));
            Assert.That(historical.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October1));
            Assert.That(historical.EffectivePeriod.EffectiveTo, Is.EqualTo(October5));
            Assert.That(historical.Definition, Is.SameAs(callerHeld));
            Assert.That(historical.Definition.SupportZones[0].Level, Is.EqualTo(100m));
            Assert.That(historical.Definition.ResistanceZones[0].Level, Is.EqualTo(205m));
            Assert.That(historical.Definition.SupportZones[0].Conditions[0].Type, Is.EqualTo(ChartConditionType.BuyZone));
            Assert.That(rule.EffectiveAt(October5)!.Definition.SupportZones[0].Level, Is.EqualTo(110m));
        });
    }

    [Test]
    public void Schedule_AfterMutationAttemptsOnValidDraftCollections_CommitsOriginalValidDefinition()
    {
        MonitoringRule rule = CreateRuleWithCurrentRevision();
        ChartAnalysisDefinition valid = CreateDefinition(110m);
        rule.CreateDraft(SecondRevisionId, valid, October5, Author, null, null);
        ChartZone overlappingZone = CreateSupportZone(205m);
        ChartCondition misplacedCondition = CreateDefinition(100m).ResistanceZones[0].Conditions[0];
        Revision<ChartAnalysisDefinition> draft = rule.FindRevision(SecondRevisionId)!;
        IList<ChartZone> exposedZones = (IList<ChartZone>)draft.Definition.SupportZones;
        IList<ChartCondition> exposedConditions = (IList<ChartCondition>)draft.Definition.SupportZones[0].Conditions;

        Assert.That(() => exposedZones[0] = overlappingZone, Throws.TypeOf<NotSupportedException>());
        Assert.That(() => exposedConditions[1] = misplacedCondition, Throws.TypeOf<NotSupportedException>());
        Result result = rule.Schedule(SecondRevisionId, October6At10, null, ContinuationId, October5);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rule.EffectiveAt(October6At10)!.Definition, Is.SameAs(valid));
            Assert.That(valid.SupportZones[0].Level, Is.EqualTo(110m));
            Assert.That(valid.SupportZones[0].Upper, Is.LessThan(valid.ResistanceZones[0].Lower));
            Assert.That(
                valid.SupportZones[0].Conditions.Select(condition => condition.Type),
                Is.EqualTo(new[] { ChartConditionType.BuyZone, ChartConditionType.SupportLoss }));
        });
    }

    private static MonitoringRule CreateRule()
    {
        return MonitoringRule.Create(
            RuleId,
            InstrumentId,
            FirstRevisionId,
            CreateDefinition(100m),
            October1,
            Author).Value;
    }

    private static MonitoringRule CreateRuleWithCurrentRevision()
    {
        return CreateRule();
    }

    private static MonitoringRule CreateRuleWithScheduledRevision()
    {
        MonitoringRule rule = CreateRule();
        rule.CreateDraft(SecondRevisionId, CreateDefinition(110m), October5, Author, "raise support", null);
        rule.Schedule(SecondRevisionId, October6At10, null, ContinuationId, October5);

        return rule;
    }

    private static ChartAnalysisDefinition CreateDefinition(decimal supportLevel)
    {
        ChartZone resistance = ChartZone.Create(
            ChartAnalysisIdentifier.From("resistance-a").Value,
            200m,
            205m,
            210m,
            [ChartCondition.Create(ChartConditionType.Breakout, ChartAnalysisIdentifier.From("publish-signal").Value).Value]).Value;

        return ChartAnalysisDefinition.Create(4, [CreateSupportZone(supportLevel)], [resistance]).Value;
    }

    private static ChartZone CreateSupportZone(decimal level)
    {
        return ChartZone.Create(
            ChartAnalysisIdentifier.From("support-a").Value,
            level - 5m,
            level,
            level + 5m,
            [
                ChartCondition.Create(ChartConditionType.BuyZone, ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                ChartCondition.Create(ChartConditionType.SupportLoss, ChartAnalysisIdentifier.From("publish-signal").Value).Value
            ]).Value;
    }
}
