using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using static TradingEngine.Domain.Tests.Revisions.SyntheticTimelines;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class RevisionTimelineAmendTests
{
    private static readonly Instant Now = October(5, 9);

    [Test]
    public void AmendScheduledRevision_WithoutDefinitionOrStart_ReturnsNoChangeRequested()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 20, October(10), Now);

        Result result = timeline.AmendScheduledRevision(future.Id, null, "reason only", null, Now);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.NoChangeRequested));
            Assert.That(future.ChangeReason, Is.Null);
        });
    }

    [Test]
    public void AmendScheduledRevision_WithUnknownRevisionAndNoChange_ReturnsNoChangeRequested()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));

        Result result = timeline.AmendScheduledRevision(Id(99), null, null, null, Now);

        Assert.That(result.Error, Is.EqualTo(RevisionErrors.NoChangeRequested));
    }

    [Test]
    public void AmendScheduledRevision_WithUnknownRevision_ReturnsNotFound()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));

        Result result = timeline.AmendScheduledRevision(Id(99), Limit(20), null, null, Now);

        Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotFound));
    }

    [Test]
    public void AmendScheduledRevision_WithDefinitionAndInvalidStart_LeavesDefinitionAndPeriodsUnchanged()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> origin = timeline.Revisions[0];
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 20, October(10), Now);

        Result result = timeline.AmendScheduledRevision(
            future.Id,
            Limit(25),
            "combined",
            October(4),
            Now);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.Backdated));
            Assert.That(future.Definition.Limit, Is.EqualTo(20));
            Assert.That(future.ChangeReason, Is.Null);
            Assert.That(future.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(10)));
            Assert.That(origin.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(10)));
        });
    }

    [Test]
    public void AmendScheduledRevision_WithDefinitionAndValidStart_AppliesBothAtomically()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> origin = timeline.Revisions[0];
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 20, October(10), Now);

        Result result = timeline.AmendScheduledRevision(
            future.Id,
            Limit(25),
            "combined",
            October(12),
            Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(future.Definition.Limit, Is.EqualTo(25));
            Assert.That(future.ChangeReason, Is.EqualTo("combined"));
            Assert.That(future.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(12)));
            Assert.That(origin.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(12)));
        });
    }

    [Test]
    public void AmendScheduledRevision_WithStartAndReasonOnly_ReschedulesAndRecordsReason()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 20, October(10), Now);

        Result result = timeline.AmendScheduledRevision(future.Id, null, "moved", October(11), Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(future.Definition.Limit, Is.EqualTo(20));
            Assert.That(future.ChangeReason, Is.EqualTo("moved"));
            Assert.That(future.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(11)));
        });
    }

    [Test]
    public void AmendScheduledRevision_OnBegunRevision_ReturnsPeriodBegunWithoutChanges()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> origin = timeline.Revisions[0];

        Result result = timeline.AmendScheduledRevision(origin.Id, Limit(25), null, October(12), Now);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(origin.Definition.Limit, Is.EqualTo(10));
            Assert.That(origin.EffectivePeriod!.EffectiveFrom, Is.EqualTo(October(1)));
        });
    }
}
