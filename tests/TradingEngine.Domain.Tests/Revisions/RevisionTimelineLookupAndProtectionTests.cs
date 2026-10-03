using System.Reflection;
using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using static TradingEngine.Domain.Tests.Revisions.SyntheticTimelines;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class RevisionTimelineLookupAndProtectionTests
{
    [Test]
    public void EffectiveAt_AtSharedBoundary_ReturnsSuccessor()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> original = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> successor = Schedule(timeline, 2, 20, October(6, 10), October(5));

        Assert.Multiple(() =>
        {
            Assert.That(timeline.EffectiveAt(October(6, 10) - Duration.FromNanoseconds(1)), Is.SameAs(original));
            Assert.That(timeline.EffectiveAt(October(6, 10)), Is.SameAs(successor));
            Assert.That(timeline.EffectiveAt(October(6, 10) + Duration.FromNanoseconds(1)), Is.SameAs(successor));
        });
    }

    [Test]
    public void EffectiveAt_MinimumCommittedPeriod_SelectsInsideAndEndBoundaries()
    {
        Instant start = October(6, 10);
        Instant end = start.PlusTicks(1);
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> first = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> middle = Schedule(timeline, 2, 20, start, October(5));
        Revision<SyntheticLimitDefinition> last = Schedule(timeline, 3, 30, end, October(5));

        Assert.Multiple(() =>
        {
            Assert.That(middle.EffectivePeriod!.EffectiveFrom, Is.EqualTo(start));
            Assert.That(middle.EffectivePeriod!.EffectiveTo, Is.EqualTo(end));
            Assert.That(timeline.EffectiveAt(start), Is.SameAs(middle));
            Assert.That(timeline.EffectiveAt(start + Duration.FromNanoseconds(50)), Is.SameAs(middle));
            Assert.That(timeline.EffectiveAt(end - Duration.FromNanoseconds(1)), Is.SameAs(middle));
            Assert.That(timeline.EffectiveAt(end), Is.SameAs(last));
            Assert.That(first.EffectivePeriod!.EffectiveTo, Is.EqualTo(start));
            Assert.That(last.EffectivePeriod!.IsOpenEnded, Is.True);
        });
    }

    [Test]
    public void EffectiveAt_BeforeFirstRevision_ReturnsNull()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Apply(timeline, 1, 10, October(1));

        Assert.That(timeline.EffectiveAt(October(1) - Duration.FromSeconds(1)), Is.Null);
    }

    [Test]
    public void EffectiveAt_OnEmptyTimeline_ReturnsNull()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();

        Assert.That(timeline.EffectiveAt(October(1)), Is.Null);
    }

    [Test]
    public void EffectiveAt_HistoricalInstantAfterRepeatedSplits_ReturnsOriginalDefinitions()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> first = Apply(timeline, 1, 10, October(1));
        SyntheticLimitDefinition firstDefinition = first.Definition;
        Revision<SyntheticLimitDefinition> second = Apply(timeline, 2, 20, October(3));
        Revision<SyntheticLimitDefinition> third = Apply(timeline, 3, 30, October(5));

        Assert.Multiple(() =>
        {
            Assert.That(timeline.EffectiveAt(October(2)), Is.SameAs(first));
            Assert.That(timeline.EffectiveAt(October(2))!.Definition, Is.SameAs(firstDefinition));
            Assert.That(timeline.EffectiveAt(October(4)), Is.SameAs(second));
            Assert.That(timeline.EffectiveAt(October(6)), Is.SameAs(third));
            Assert.That(first.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(3)));
            Assert.That(second.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(5)));
            Assert.That(third.EffectivePeriod!.EffectiveTo, Is.Null);
            Assert.That(
                timeline.Revisions.Select(revision => revision.RevisionNumber),
                Is.EqualTo(new int?[] { 1, 2, 3 }));
        });
    }

    [Test]
    public void Find_WithDraftOrCommittedId_ReturnsMatchingRevision()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Revision<SyntheticLimitDefinition> committed = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 2, 20, October(2));

        Assert.Multiple(() =>
        {
            Assert.That(timeline.Find(committed.Id), Is.SameAs(committed));
            Assert.That(timeline.Find(draft.Id), Is.SameAs(draft));
            Assert.That(timeline.Find(Id(9)), Is.Null);
        });
    }

    [Test]
    public void Revisions_WhenRead_ReturnsReadOnlySnapshotDetachedFromTimeline()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Apply(timeline, 1, 10, October(1));

        IReadOnlyList<Revision<SyntheticLimitDefinition>> snapshot = timeline.Revisions;
        Apply(timeline, 2, 20, October(2));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(timeline.Revisions, Has.Count.EqualTo(2));
            Assert.That(snapshot, Is.Not.SameAs(timeline.Revisions));
            Assert.That(((ICollection<Revision<SyntheticLimitDefinition>>)snapshot).IsReadOnly, Is.True);
        });
    }

    [Test]
    public void Drafts_WhenRead_ReturnsReadOnlySnapshotDetachedFromTimeline()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        Draft(timeline, 1, 10, October(1));

        IReadOnlyList<Revision<SyntheticLimitDefinition>> snapshot = timeline.Drafts;
        Draft(timeline, 2, 20, October(1));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(timeline.Drafts, Has.Count.EqualTo(2));
            Assert.That(((ICollection<Revision<SyntheticLimitDefinition>>)snapshot).IsReadOnly, Is.True);
        });
    }

    [Test]
    public void CreateDraft_WithCallerHeldCollection_LaterMutationDoesNotAffectStoredDefinition()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        string[] tags = ["synthetic"];
        SyntheticLimitDefinition definition = new(10, tags);
        Revision<SyntheticLimitDefinition> draft = timeline
            .CreateDraft(Id(1), definition, October(1), Author, null, null)
            .Value;
        timeline.ApplyNow(draft.Id, October(1));

        tags[0] = "mutated";

        Assert.That(timeline.EffectiveAt(October(2))!.Definition.Tags, Is.EqualTo(new[] { "synthetic" }));
    }

    [Test]
    public void EffectiveAt_AfterIndexAssignmentThroughExposedTags_ReturnsUnchangedDefinition()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        SyntheticLimitDefinition definition = new(10, ["synthetic"]);
        Revision<SyntheticLimitDefinition> revision = timeline
            .CreateDraft(Id(1), definition, October(1), Author, null, null)
            .Value;
        timeline.ApplyNow(revision.Id, October(1));
        Apply(timeline, 2, 20, October(3));
        IList<string> callerHeldTags = (IList<string>)definition.Tags;
        IList<string> exposedTags = (IList<string>)timeline.EffectiveAt(October(2))!.Definition.Tags;

        Assert.That(() => callerHeldTags[0] = "mutated", Throws.TypeOf<NotSupportedException>());
        Assert.That(() => exposedTags[0] = "mutated", Throws.TypeOf<NotSupportedException>());
        Assert.That(() => exposedTags.Add("extra"), Throws.TypeOf<NotSupportedException>());

        Assert.Multiple(() =>
        {
            Assert.That(timeline.EffectiveAt(October(2))!.Definition, Is.SameAs(definition));
            Assert.That(timeline.EffectiveAt(October(2))!.Definition.Tags, Is.EqualTo(new[] { "synthetic" }));
            Assert.That(revision.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(3)));
        });
    }

    [Test]
    public void EditDraft_WithReplacementDefinition_DoesNotAlterPreviouslyCommittedDefinitionReference()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = new();
        SyntheticLimitDefinition committedDefinition = Limit(10);
        Revision<SyntheticLimitDefinition> committed = timeline
            .CreateDraft(Id(1), committedDefinition, October(1), Author, null, null)
            .Value;
        timeline.ApplyNow(committed.Id, October(1));
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 2, 20, October(2));

        timeline.EditDraft(draft.Id, Limit(99), null, null);

        Assert.Multiple(() =>
        {
            Assert.That(committed.Definition, Is.SameAs(committedDefinition));
            Assert.That(committed.Definition.Limit, Is.EqualTo(10));
        });
    }

    [Test]
    public void RevisionType_ByDesign_ExposesNoPublicMutators()
    {
        Type revisionType = typeof(Revision<SyntheticLimitDefinition>);

        string[] publicSetters = revisionType
            .GetProperties()
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToArray();
        string[] publicMutatingMethods = revisionType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.IsSpecialName is false)
            .Where(method => method.ReturnType == typeof(void))
            .Select(method => method.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(publicSetters, Is.Empty);
            Assert.That(publicMutatingMethods, Is.Empty);
        });
    }

    [Test]
    public void RevisionTimeline_WithDifferentDefinitionType_AppliesSameMechanics()
    {
        RevisionTimeline<SyntheticLabelDefinition> timeline = new();
        Revision<SyntheticLabelDefinition> first = timeline
            .CreateDraft(Id(1), new SyntheticLabelDefinition("alpha"), October(1), Author, null, null)
            .Value;
        timeline.ApplyNow(first.Id, October(1));
        Revision<SyntheticLabelDefinition> second = timeline
            .CreateDraft(Id(2), new SyntheticLabelDefinition("beta"), October(5), Author, "rename", null)
            .Value;

        Result scheduled = timeline.Schedule(second.Id, October(6, 10), October(5));
        Result edited = timeline.EditScheduledRevision(second.Id, new SyntheticLabelDefinition("gamma"), null, October(6, 10));

        Assert.Multiple(() =>
        {
            Assert.That(scheduled.IsSuccess, Is.True);
            Assert.That(edited.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(first.EffectivePeriod!.EffectiveTo, Is.EqualTo(October(6, 10)));
            Assert.That(timeline.EffectiveAt(October(3))!.Definition.Label, Is.EqualTo("alpha"));
            Assert.That(timeline.EffectiveAt(October(6, 10))!.Definition.Label, Is.EqualTo("beta"));
            Assert.That(second.RevisionNumber, Is.EqualTo(2));
        });
    }
}
