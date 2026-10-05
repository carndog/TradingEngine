using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using static TradingEngine.Domain.Tests.Revisions.SyntheticTimelines;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class RevisionTimelineFutureEditTests
{
    private static readonly Instant Start = October(6, 10);

    [Test]
    public void EditScheduledRevision_BeforeStartBoundary_ReplacesDefinitionAndReason()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 1, 10, Start, October(5));

        Result result = timeline.EditScheduledRevision(
            future.Id,
            Limit(15),
            "adjusted before start",
            Start - Duration.FromSeconds(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(future.Definition.Limit, Is.EqualTo(15));
            Assert.That(future.ChangeReason, Is.EqualTo("adjusted before start"));
            Assert.That(future.EffectivePeriod!.EffectiveFrom, Is.EqualTo(Start));
        });
    }

    [Test]
    public void EditScheduledRevision_AtExactStartBoundary_ReturnsPeriodBegunError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 1, 10, Start, October(5));

        Result result = timeline.EditScheduledRevision(future.Id, Limit(15), null, Start);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(future.Definition.Limit, Is.EqualTo(10));
        });
    }

    [Test]
    public void EditScheduledRevision_AfterStart_ReturnsPeriodBegunError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));

        Result result = timeline.EditScheduledRevision(current.Id, Limit(15), null, October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(current.Definition.Limit, Is.EqualTo(10));
        });
    }

    [Test]
    public void EditScheduledRevision_WithDraftId_ReturnsNotCommittedError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 1, 10, October(1));

        Result result = timeline.EditScheduledRevision(draft.Id, Limit(15), null, October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotCommitted));
    }

    [Test]
    public void EditScheduledRevision_WithUnknownId_ReturnsNotFoundError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();

        Result result = timeline.EditScheduledRevision(Id(9), Limit(15), null, October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotFound));
    }

    [Test]
    public void Reschedule_FutureRevisionLater_ExtendsAdjacentPredecessorToNewStart()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 20, Start, October(5));

        Result result = timeline.Reschedule(future.Id, October(8), October(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(current.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(8)));
            Assert.That(future.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(8)));
            Assert.That(future.EffectivePeriod.EffectiveTo, Is.Null);
            Assert.That(timeline.EffectiveAt(October(7)), Is.SameAs(current));
            Assert.That(timeline.EffectiveAt(October(8)), Is.SameAs(future));
        });
    }

    [Test]
    public void Reschedule_FutureRevisionEarlier_ShrinksAdjacentPredecessorWithoutChangingHistory()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 20, October(8), October(5));

        Result result = timeline.Reschedule(future.Id, Start, October(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(current.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(1)));
            Assert.That(current.EffectivePeriod.EffectiveTo, Is.EqualTo(Start));
            Assert.That(future.EffectivePeriod!.EffectiveFrom, Is.EqualTo(Start));
            Assert.That(timeline.EffectiveAt(October(4)), Is.SameAs(current));
        });
    }

    [Test]
    public void Reschedule_ToPredecessorStart_ReturnsInvalidPeriodErrorAndLeavesStateUnchanged()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> first = Schedule(timeline, 1, 10, October(6), October(5));
        Revision<SyntheticLimitDefinition> second = Schedule(timeline, 2, 20, October(8), October(5));

        Result result = timeline.Reschedule(second.Id, October(6), October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.InvalidPeriod));
            Assert.That(first.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(8)));
            Assert.That(second.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(8)));
        });
    }

    [Test]
    public void Reschedule_AtOrBeyondOwnEnd_ReturnsInvalidPeriodError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> first = Schedule(timeline, 1, 10, October(6), October(5));
        Revision<SyntheticLimitDefinition> second = Schedule(timeline, 2, 20, October(8), October(5));
        Revision<SyntheticLimitDefinition> inserted = Draft(timeline, 3, 30, October(5));
        timeline.Schedule(inserted.Id, October(6, 12), October(7), Id(90), October(5));

        Result result = timeline.Reschedule(inserted.Id, October(7), October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.InvalidPeriod));
            Assert.That(inserted.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(6, 12)));
            Assert.That(inserted.EffectivePeriod.EffectiveTo, Is.EqualTo(October(7)));
            Assert.That(second.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(8)));
            Assert.That(first.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(6)));
        });
    }

    [Test]
    public void Reschedule_OriginRevision_ReturnsCoverageOriginProtectedError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> origin = Schedule(timeline, 1, 30, October(10), October(5));

        Result result = timeline.Reschedule(origin.Id, October(12), October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.CoverageOriginProtected));
            Assert.That(origin.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(10)));
        });
    }

    [Test]
    public void Reschedule_BeforeNow_ReturnsBackdatedError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 1, 10, Start, October(5));

        Result result = timeline.Reschedule(future.Id, October(4), October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.Backdated));
            Assert.That(future.EffectivePeriod!.EffectiveFrom, Is.EqualTo(Start));
        });
    }

    [Test]
    public void Reschedule_BegunRevision_ReturnsPeriodBegunError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));

        Result result = timeline.Reschedule(current.Id, October(8), October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
    }

    [Test]
    public void RemoveScheduledRevision_FutureRevision_ExtendsPredecessorToRemovedEndBoundary()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> later = Schedule(timeline, 2, 30, October(10), October(5));
        Revision<SyntheticLimitDefinition> middle = Schedule(timeline, 3, 20, Start, October(5), October(10));

        Result result = timeline.RemoveScheduledRevision(middle.Id, October(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(timeline.Find(middle.Id), Is.Null);
            Assert.That(current.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(1)));
            Assert.That(current.EffectivePeriod.EffectiveTo, Is.EqualTo(October(10)));
            Assert.That(later.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(10)));
            Assert.That(timeline.Revisions, Is.EqualTo(new[] { current, later }));
            Assert.That(current.RevisionNumber, Is.EqualTo(1));
            Assert.That(later.RevisionNumber, Is.EqualTo(2));
            Assert.That(timeline.EffectiveAt(October(7)), Is.SameAs(current));
        });
    }

    [Test]
    public void RemoveScheduledRevision_OriginRevision_ReturnsCoverageOriginProtectedError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> origin = Schedule(timeline, 1, 30, October(10), October(5));

        Result result = timeline.RemoveScheduledRevision(origin.Id, October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.CoverageOriginProtected));
            Assert.That(timeline.Revisions, Is.EqualTo(new[] { origin }));
        });
    }

    [Test]
    public void RemoveScheduledRevision_BegunRevision_ReturnsPeriodBegunError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));

        Result result = timeline.RemoveScheduledRevision(current.Id, October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(timeline.Revisions, Is.EqualTo(new[] { current }));
        });
    }

    [Test]
    public void RemoveScheduledRevision_AtExactStartBoundary_ReturnsPeriodBegunError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 1, 10, Start, October(5));

        Result result = timeline.RemoveScheduledRevision(future.Id, Start);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
    }

    [Test]
    public void RemoveScheduledRevision_WithDraftId_ReturnsNotCommittedError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 1, 10, October(1));

        Result result = timeline.RemoveScheduledRevision(draft.Id, October(5));

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotCommitted));
            Assert.That(timeline.Drafts, Is.EqualTo(new[] { draft }));
        });
    }
}
