using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using static TradingEngine.Domain.Tests.Revisions.SyntheticTimelines;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class RevisionTimelineAgreedExampleTests
{
    private static readonly Instant Monday = October(5, 9);
    private static readonly Instant Tuesday = October(6);
    private static readonly Instant Wednesday = October(7);
    private static readonly Instant Thursday = October(8);

    [Test]
    public void Schedule_ExampleOne_SingleOpenEndedRevisionWithBoundedInsert_ProducesPrefixInsertAndContinuation()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> a = timeline.Revisions[0];
        Revision<SyntheticLimitDefinition> b = Draft(timeline, 2, 20, Monday);

        Result result = timeline.Schedule(b.Id, Tuesday, Thursday, Id(900), Monday);

        Assert.That(result.IsSuccess, Is.True);
        IReadOnlyList<Revision<SyntheticLimitDefinition>> revisions = timeline.Revisions;
        Assert.Multiple(() =>
        {
            Assert.That(revisions, Has.Count.EqualTo(3));
            Assert.That(revisions[0].Id, Is.EqualTo(a.Id));
            Assert.That(revisions[0].Definition.Limit, Is.EqualTo(10));
            Assert.That(revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(Tuesday));
            Assert.That(revisions[1].Id, Is.EqualTo(b.Id));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(Tuesday));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveTo, Is.EqualTo(Thursday));
            Assert.That(revisions[2].Id, Is.EqualTo(Id(900)));
            Assert.That(revisions[2].Definition.Limit, Is.EqualTo(10));
            Assert.That(revisions[2].EffectivePeriod!.EffectiveFrom, Is.EqualTo(Thursday));
            Assert.That(revisions[2].EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(revisions[2].CreatedAt, Is.EqualTo(Monday));
            Assert.That(revisions[2].CreatedBy, Is.EqualTo(Author));
            Assert.That(revisions.Select(revision => revision.RevisionNumber), Is.EqualTo(new int?[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void Schedule_ExampleTwo_BoundedInsertSpanningBoundary_TrimsBlueAndMovesGreenStart()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> blue = timeline.Revisions[0];
        Revision<SyntheticLimitDefinition> green = Schedule(timeline, 2, 20, Wednesday, October(2));
        Revision<SyntheticLimitDefinition> red = Draft(timeline, 3, 30, Monday);

        Result result = timeline.Schedule(red.Id, Tuesday, Thursday, Id(900), Monday);

        Assert.That(result.IsSuccess, Is.True);
        IReadOnlyList<Revision<SyntheticLimitDefinition>> revisions = timeline.Revisions;
        Assert.Multiple(() =>
        {
            Assert.That(revisions, Has.Count.EqualTo(3));
            Assert.That(revisions[0].Id, Is.EqualTo(blue.Id));
            Assert.That(revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(Tuesday));
            Assert.That(revisions[1].Id, Is.EqualTo(red.Id));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(Tuesday));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveTo, Is.EqualTo(Thursday));
            Assert.That(revisions[2].Id, Is.EqualTo(green.Id));
            Assert.That(revisions[2].Definition.Limit, Is.EqualTo(20));
            Assert.That(revisions[2].EffectivePeriod!.EffectiveFrom, Is.EqualTo(Thursday));
            Assert.That(revisions[2].EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(timeline.Find(Id(900)), Is.Null);
        });
    }

    [Test]
    public void Schedule_ExampleThree_OpenEndedInsertBeforeFutureRevision_RemovesCoveredGreen()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Revision<SyntheticLimitDefinition> blue = timeline.Revisions[0];
        Revision<SyntheticLimitDefinition> green = Schedule(timeline, 2, 20, Wednesday, October(2));
        Revision<SyntheticLimitDefinition> red = Draft(timeline, 3, 30, Monday);

        Result result = timeline.Schedule(red.Id, Tuesday, null, Id(900), Monday);

        Assert.That(result.IsSuccess, Is.True);
        IReadOnlyList<Revision<SyntheticLimitDefinition>> revisions = timeline.Revisions;
        Assert.Multiple(() =>
        {
            Assert.That(revisions, Has.Count.EqualTo(2));
            Assert.That(revisions[0].Id, Is.EqualTo(blue.Id));
            Assert.That(revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(Tuesday));
            Assert.That(revisions[1].Id, Is.EqualTo(red.Id));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveFrom, Is.EqualTo(Tuesday));
            Assert.That(revisions[1].EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(timeline.Find(green.Id), Is.Null);
            Assert.That(timeline.EffectiveAt(Wednesday)!.Id, Is.EqualTo(red.Id));
        });
    }

    [Test]
    public void Schedule_EveryExample_LeavesExactlyOneRevisionEffectiveAtEveryProbedInstant()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Timeline(1, 10, October(1));
        Schedule(timeline, 2, 20, Wednesday, October(2));
        Revision<SyntheticLimitDefinition> red = Draft(timeline, 3, 30, Monday);
        timeline.Schedule(red.Id, Tuesday, Thursday, Id(900), Monday);

        Instant[] probes =
        [
            October(1),
            Monday,
            Tuesday - Duration.FromNanoseconds(1),
            Tuesday,
            Wednesday,
            Thursday - Duration.FromNanoseconds(1),
            Thursday,
            October(20)
        ];

        Assert.Multiple(() =>
        {
            foreach (Instant probe in probes)
            {
                int count = timeline.Revisions.Count(revision => revision.IsEffectiveAt(probe));
                Assert.That(count, Is.EqualTo(1));
            }
        });
    }
}
