using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using static TradingEngine.Domain.Tests.Revisions.SyntheticTimelines;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class RevisionTimelineCommitTests
{
    [Test]
    public void ApplyNow_OnEmptyTimeline_CommitsOpenEndedPeriodStartingNow()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> draft = Draft(
            timeline,
            1,
            10,
            October(1),
            new RevisionProposal(October(6, 10), null));

        Result result = timeline.ApplyNow(draft.Id, October(3, 9));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(draft.IsDraft, Is.False);
            Assert.That(draft.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(3, 9)));
            Assert.That(draft.EffectivePeriod.EffectiveTo, Is.Null);
            Assert.That(draft.Proposal, Is.Null);
            Assert.That(draft.RevisionNumber, Is.EqualTo(1));
            Assert.That(draft.CreatedAt, Is.EqualTo(October(1)));
            Assert.That(timeline.Drafts, Is.Empty);
            Assert.That(timeline.Revisions, Is.EqualTo(new[] { draft }));
            Assert.That(timeline.EffectiveAt(October(3, 9)), Is.SameAs(draft));
        });
    }

    [Test]
    public void ApplyNow_WithCurrentOpenEndedRevision_SplitsAtNowAndSuccessorInheritsOpenEnd()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> original = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 2, 20, October(4));
        Instant now = October(5, 12);

        Result result = timeline.ApplyNow(draft.Id, now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(original.Id, Is.EqualTo(Id(1)));
            Assert.That(original.Definition.Limit, Is.EqualTo(10));
            Assert.That(original.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(1)));
            Assert.That(original.EffectivePeriod.EffectiveTo, Is.EqualTo(now));
            Assert.That(draft.EffectivePeriod!.EffectiveFrom, Is.EqualTo(now));
            Assert.That(draft.EffectivePeriod.EffectiveTo, Is.Null);
            Assert.That(timeline.EffectiveAt(now - Duration.FromSeconds(1)), Is.SameAs(original));
            Assert.That(timeline.EffectiveAt(now), Is.SameAs(draft));
        });
    }

    [Test]
    public void ApplyNow_WithOtherDrafts_LeavesOtherDraftsUncommitted()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        RevisionProposal sharedProposal = new(October(6, 10), null);
        Revision<SyntheticLimitDefinition> chosen = Draft(timeline, 1, 10, October(5), sharedProposal);
        Revision<SyntheticLimitDefinition> alternative = Draft(timeline, 2, 20, October(5), sharedProposal);

        Result result = timeline.Schedule(chosen.Id, October(6, 10), October(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(alternative.IsDraft, Is.True);
            Assert.That(alternative.Proposal, Is.EqualTo(sharedProposal));
            Assert.That(timeline.Drafts, Is.EqualTo(new[] { alternative }));
            Assert.That(timeline.Revisions, Is.EqualTo(new[] { chosen }));
        });
    }

    [Test]
    public void Schedule_InFuture_CommitsRevisionThatIsNotYetEffective()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 1, 10, October(5));

        Result result = timeline.Schedule(draft.Id, October(6, 10), October(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(draft.IsDraft, Is.False);
            Assert.That(draft.HasBegun(October(5)), Is.False);
            Assert.That(draft.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(6, 10)));
            Assert.That(timeline.EffectiveAt(October(5)), Is.Null);
            Assert.That(timeline.EffectiveAt(October(6, 10)), Is.SameAs(draft));
        });
    }

    [Test]
    public void Schedule_BeforeNow_ReturnsBackdatedErrorAndLeavesStateUnchanged()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        RevisionProposal proposal = new(October(6, 10), null);
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 2, 20, October(5), proposal);

        Result result = timeline.Schedule(draft.Id, October(6, 10), October(8));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.Backdated));
            Assert.That(draft.IsDraft, Is.True);
            Assert.That(draft.Proposal, Is.EqualTo(proposal));
            Assert.That(timeline.Drafts, Is.EqualTo(new[] { draft }));
            Assert.That(timeline.Revisions, Is.EqualTo(new[] { current }));
            Assert.That(current.EffectivePeriod!.IsOpenEnded, Is.True);
        });
    }

    [Test]
    public void ApplyNow_AfterTimePassedProposedStart_UsesCapturedNowRatherThanProposal()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> draft = Draft(
            timeline,
            1,
            10,
            October(5),
            new RevisionProposal(October(6, 10), null));

        Result result = timeline.ApplyNow(draft.Id, October(8));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(draft.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(8)));
            Assert.That(timeline.EffectiveAt(October(7)), Is.Null);
        });
    }

    [Test]
    public void Schedule_AfterTimelineChangedSinceDrafting_ValidatesAgainstCurrentTimeline()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> first = Draft(timeline, 2, 20, October(5));
        Revision<SyntheticLimitDefinition> second = Draft(timeline, 3, 30, October(5));
        timeline.Schedule(first.Id, October(6, 10), October(5));

        Result result = timeline.Schedule(second.Id, October(6, 10), October(5, 1));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.StartConflict));
            Assert.That(second.IsDraft, Is.True);
            Assert.That(timeline.Revisions, Has.Count.EqualTo(2));
            Assert.That(timeline.EffectiveAt(October(6, 10)), Is.SameAs(first));
        });
    }

    [Test]
    public void Schedule_AtStartOfCurrentRevisionBeginningNow_ReturnsStartConflictError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 2, 20, October(1));

        Result result = timeline.ApplyNow(draft.Id, October(1));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.StartConflict));
            Assert.That(draft.IsDraft, Is.True);
            Assert.That(current.EffectivePeriod!.IsOpenEnded, Is.True);
        });
    }

    [Test]
    public void Schedule_InsideExistingPeriod_SuccessorInheritsEndBoundaryAndPreservesLaterRevision()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> original = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> later = Schedule(timeline, 2, 30, October(10), October(5));
        Revision<SyntheticLimitDefinition> inserted = Draft(timeline, 3, 20, October(5));

        Result result = timeline.Schedule(inserted.Id, October(6, 10), October(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(original.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(1)));
            Assert.That(original.EffectivePeriod.EffectiveTo, Is.EqualTo(October(6, 10)));
            Assert.That(inserted.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(6, 10)));
            Assert.That(inserted.EffectivePeriod.EffectiveTo, Is.EqualTo(October(10)));
            Assert.That(later.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(10)));
            Assert.That(later.EffectivePeriod.EffectiveTo, Is.Null);
            Assert.That(timeline.Revisions, Is.EqualTo(new[] { original, inserted, later }));
        });
    }

    [Test]
    public void Schedule_InsideEntirelyFutureRevision_SplitsFutureRevision()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 1, 10, October(10), October(5));
        Revision<SyntheticLimitDefinition> inserted = Draft(timeline, 2, 20, October(5));

        Result result = timeline.Schedule(inserted.Id, October(12), October(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(future.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(12)));
            Assert.That(inserted.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(12)));
            Assert.That(inserted.EffectivePeriod.EffectiveTo, Is.Null);
        });
    }

    [Test]
    public void Schedule_InGapBeforeFutureRevision_EndsAtNextRevisionStart()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> later = Schedule(timeline, 1, 30, October(10), October(5));
        Revision<SyntheticLimitDefinition> earlier = Draft(timeline, 2, 20, October(5));

        Result result = timeline.Schedule(earlier.Id, October(6), October(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(earlier.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(6)));
            Assert.That(earlier.EffectivePeriod.EffectiveTo, Is.EqualTo(October(10)));
            Assert.That(later.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(10)));
            Assert.That(timeline.EffectiveAt(October(5, 23)), Is.Null);
            Assert.That(timeline.EffectiveAt(October(10)), Is.SameAs(later));
        });
    }

    [Test]
    public void Schedule_WithCommittedRevisionId_ReturnsNotDraftError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));

        Result result = timeline.Schedule(current.Id, October(6), October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotDraft));
    }

    [Test]
    public void Schedule_WithUnknownDraftId_ReturnsNotFoundError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();

        Result result = timeline.Schedule(Id(9), October(6), October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotFound));
    }

    [Test]
    public void Schedule_InsertedBetweenFutureRevisions_RenumbersOnlyFutureRevisionsAndKeepsIds()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> begun = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 30, October(10), October(5));
        Revision<SyntheticLimitDefinition> inserted = Draft(timeline, 3, 20, October(5));

        timeline.Schedule(inserted.Id, October(7), October(5));

        Assert.Multiple(() =>
        {
            Assert.That(begun.Id, Is.EqualTo(Id(1)));
            Assert.That(begun.RevisionNumber, Is.EqualTo(1));
            Assert.That(inserted.Id, Is.EqualTo(Id(3)));
            Assert.That(inserted.RevisionNumber, Is.EqualTo(2));
            Assert.That(future.Id, Is.EqualTo(Id(2)));
            Assert.That(future.RevisionNumber, Is.EqualTo(3));
        });
    }

    [Test]
    public void ApplyNow_SplittingCurrentRevision_KeepsOriginalIdentityAndNumber()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> original = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> successor = Draft(timeline, 2, 20, October(5));

        timeline.ApplyNow(successor.Id, October(5));

        Assert.Multiple(() =>
        {
            Assert.That(original.Id, Is.EqualTo(Id(1)));
            Assert.That(original.RevisionNumber, Is.EqualTo(1));
            Assert.That(successor.Id, Is.EqualTo(Id(2)));
            Assert.That(successor.RevisionNumber, Is.EqualTo(2));
            Assert.That(successor.Id, Is.Not.EqualTo(original.Id));
        });
    }
}
