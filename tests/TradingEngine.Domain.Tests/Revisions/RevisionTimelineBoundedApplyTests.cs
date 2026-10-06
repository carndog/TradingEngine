using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using static TradingEngine.Domain.Tests.Revisions.SyntheticTimelines;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class RevisionTimelineBoundedApplyTests
{
    private static readonly Instant Now = October(5, 9);
    private static readonly Instant Thursday = October(8);

    [Test]
    public void ApplyNow_WithEndInsideCurrentRevision_CommitsFromNowUntilEndAndContinuesOriginal()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> original = timeline.Revisions[0];
        Revision<SyntheticLimitDefinition> temporary = Draft(timeline, 2, 20, Now);

        Result result = timeline.ApplyNow(temporary.Id, Thursday, Id(900), Now);

        Assert.That(result.IsSuccess, Is.True);
        IReadOnlyList<Revision<SyntheticLimitDefinition>> revisions = timeline.Revisions;
        Assert.Multiple(() =>
        {
            Assert.That(revisions, Has.Count.EqualTo(3));
            Assert.That(revisions[0].Id, Is.EqualTo(original.Id));
            Assert.That(revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(Now));
            Assert.That(revisions[1].Id, Is.EqualTo(temporary.Id));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(Now));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveTo, Is.EqualTo(Thursday));
            Assert.That(revisions[2].Id, Is.EqualTo(Id(900)));
            Assert.That(revisions[2].Definition.Limit, Is.EqualTo(10));
            Assert.That(revisions[2].EffectivePeriod!.EffectiveFrom, Is.EqualTo(Thursday));
            Assert.That(revisions[2].EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(timeline.EffectiveAt(Now)!.Definition.Limit, Is.EqualTo(20));
            Assert.That(timeline.EffectiveAt(Thursday)!.Definition.Limit, Is.EqualTo(10));
        });
    }

    [Test]
    public void ApplyNow_WithEndCrossingFutureRevision_ContinuesWithDefinitionApplicableAtEnd()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 20, October(7), October(2));
        Revision<SyntheticLimitDefinition> temporary = Draft(timeline, 3, 30, Now);

        Result result = timeline.ApplyNow(temporary.Id, Thursday, Id(900), Now);

        Assert.That(result.IsSuccess, Is.True);
        IReadOnlyList<Revision<SyntheticLimitDefinition>> revisions = timeline.Revisions;
        Assert.Multiple(() =>
        {
            Assert.That(revisions, Has.Count.EqualTo(3));
            Assert.That(revisions[1].Id, Is.EqualTo(temporary.Id));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveTo, Is.EqualTo(Thursday));
            Assert.That(revisions[2].Id, Is.EqualTo(future.Id));
            Assert.That(revisions[2].Definition.Limit, Is.EqualTo(20));
            Assert.That(revisions[2].EffectivePeriod!.EffectiveFrom, Is.EqualTo(Thursday));
            Assert.That(timeline.Find(Id(900)), Is.Null);
        });
    }

    [Test]
    public void ApplyNow_WithoutEnd_RemainsOpenEndedAndReplacesCoveredFuture()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> future = Schedule(timeline, 2, 20, October(7), October(2));
        Revision<SyntheticLimitDefinition> replacement = Draft(timeline, 3, 30, Now);

        Result result = timeline.ApplyNow(replacement.Id, null, Id(900), Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(timeline.Revisions, Has.Count.EqualTo(2));
            Assert.That(timeline.Revisions[1].Id, Is.EqualTo(replacement.Id));
            Assert.That(timeline.Revisions[1].EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(timeline.Find(future.Id), Is.Null);
        });
    }

    [Test]
    public void ApplyNow_WithEndNotAfterNow_ReturnsInvalidPeriodAndLeavesStateUnchanged()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> temporary = Draft(timeline, 2, 20, Now);

        Result result = timeline.ApplyNow(temporary.Id, Now, Id(900), Now);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.InvalidPeriod));
            Assert.That(timeline.Revisions, Has.Count.EqualTo(1));
            Assert.That(timeline.Revisions[0].EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(timeline.Drafts, Has.Count.EqualTo(1));
        });
    }
}
