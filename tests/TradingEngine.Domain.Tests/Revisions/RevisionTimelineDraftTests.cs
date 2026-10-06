using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using static TradingEngine.Domain.Tests.Revisions.SyntheticTimelines;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class RevisionTimelineDraftTests
{
    [Test]
    public void CreateDraft_WithValidInputs_AddsDraftOutsideCommittedTimeline()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        RevisionProposal proposal = new(October(6, 10), null);

        Result<Revision<SyntheticLimitDefinition>> result = timeline.CreateDraft(
            Id(1),
            Limit(10),
            October(1),
            Author,
            "initial proposal",
            proposal);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Id, Is.EqualTo(Id(1)));
            Assert.That(result.Value.IsDraft, Is.True);
            Assert.That(result.Value.EffectivePeriod, Is.Null);
            Assert.That(result.Value.RevisionNumber, Is.Null);
            Assert.That(result.Value.CreatedAt, Is.EqualTo(October(1)));
            Assert.That(result.Value.CreatedBy, Is.EqualTo(Author));
            Assert.That(result.Value.ChangeReason, Is.EqualTo("initial proposal"));
            Assert.That(result.Value.Proposal, Is.EqualTo(proposal));
            Assert.That(timeline.Drafts, Has.Count.EqualTo(1));
            Assert.That(timeline.Revisions, Is.Empty);
        });
    }

    [Test]
    public void CreateDraft_WithEmptyId_ReturnsIdRequiredError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();

        Result<Revision<SyntheticLimitDefinition>> result = timeline.CreateDraft(
            Guid.Empty,
            Limit(10),
            October(1),
            Author,
            null,
            null);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.IdRequired));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void CreateDraft_WithMissingCreatedBy_ReturnsCreatedByRequiredError(string? createdBy)
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();

        Result<Revision<SyntheticLimitDefinition>> result = timeline.CreateDraft(
            Id(1),
            Limit(10),
            October(1),
            createdBy,
            null,
            null);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.CreatedByRequired));
    }

    [Test]
    public void CreateDraft_WithDuplicateId_ReturnsDuplicateIdError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Draft(timeline, 1, 10, October(1));

        Result<Revision<SyntheticLimitDefinition>> result = timeline.CreateDraft(
            Id(1),
            Limit(20),
            October(2),
            Author,
            null,
            null);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.DuplicateId));
        Assert.That(timeline.Drafts, Has.Count.EqualTo(1));
    }

    [Test]
    public void CreateDraft_WithIdOfCommittedRevision_ReturnsDuplicateIdError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Apply(timeline, 1, 10, October(1));

        Result<Revision<SyntheticLimitDefinition>> result = timeline.CreateDraft(
            Id(1),
            Limit(20),
            October(2),
            Author,
            null,
            null);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.DuplicateId));
    }

    [Test]
    public void CreateDraft_WithOverlappingProposals_KeepsBothDraftsOutsideTimeline()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        RevisionProposal sharedProposal = new(October(6, 10), null);

        Revision<SyntheticLimitDefinition> first = Draft(timeline, 2, 20, October(5), sharedProposal);
        Revision<SyntheticLimitDefinition> second = Draft(timeline, 3, 30, October(5), sharedProposal);

        Assert.Multiple(() =>
        {
            Assert.That(first.Proposal, Is.EqualTo(sharedProposal));
            Assert.That(second.Proposal, Is.EqualTo(sharedProposal));
            Assert.That(timeline.Drafts, Has.Count.EqualTo(2));
            Assert.That(timeline.Revisions, Has.Count.EqualTo(1));
            Assert.That(current.EffectivePeriod!.IsOpenEnded, Is.True);
        });
    }

    [Test]
    public void EffectiveAt_WhenOnlyDraftProposalCoversInstant_ReturnsNull()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Draft(timeline, 1, 10, October(1), new RevisionProposal(October(1), null));

        Revision<SyntheticLimitDefinition>? effective = timeline.EffectiveAt(October(7));

        Assert.That(effective, Is.Null);
    }

    [Test]
    public void EffectiveAt_WhenDraftProposalOverlapsCurrentRevision_ReturnsCurrentRevision()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        Draft(timeline, 2, 20, October(5), new RevisionProposal(October(6, 10), null));

        Revision<SyntheticLimitDefinition>? effective = timeline.EffectiveAt(October(7));

        Assert.That(effective, Is.SameAs(current));
    }

    [Test]
    public void EditDraft_WithNewDefinition_ReplacesDefinitionReasonAndProposalWithoutTimelineChange()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 2, 20, October(5));
        RevisionProposal proposal = new(October(6, 10), October(8));

        Result result = timeline.EditDraft(draft.Id, Limit(25), "tightened", proposal);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(draft.Definition.Limit, Is.EqualTo(25));
            Assert.That(draft.ChangeReason, Is.EqualTo("tightened"));
            Assert.That(draft.Proposal, Is.EqualTo(proposal));
            Assert.That(draft.IsDraft, Is.True);
            Assert.That(timeline.Revisions, Has.Count.EqualTo(1));
            Assert.That(current.EffectivePeriod!.IsOpenEnded, Is.True);
            Assert.That(timeline.EffectiveAt(October(7)), Is.SameAs(current));
        });
    }

    [Test]
    public void EditDraft_WithCommittedRevisionId_ReturnsNotDraftError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));

        Result result = timeline.EditDraft(current.Id, Limit(99), null, null);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotDraft));
            Assert.That(current.Definition.Limit, Is.EqualTo(10));
        });
    }

    [Test]
    public void EditDraft_WithUnknownId_ReturnsNotFoundError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();

        Result result = timeline.EditDraft(Id(9), Limit(10), null, null);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotFound));
    }

    [Test]
    public void DeleteDraft_WithExistingDraft_RemovesDraftWithoutTimelineChange()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));
        Revision<SyntheticLimitDefinition> draft = Draft(timeline, 2, 20, October(5));
        Revision<SyntheticLimitDefinition> other = Draft(timeline, 3, 30, October(5));

        Result result = timeline.DeleteDraft(draft.Id);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(timeline.Drafts, Is.EqualTo(new[] { other }));
            Assert.That(timeline.Find(draft.Id), Is.Null);
            Assert.That(timeline.Revisions, Is.EqualTo(new[] { current }));
            Assert.That(current.EffectivePeriod!.IsOpenEnded, Is.True);
        });
    }

    [Test]
    public void DeleteDraft_WithCommittedRevisionId_ReturnsNotDraftError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();
        Revision<SyntheticLimitDefinition> current = Apply(timeline, 1, 10, October(1));

        Result result = timeline.DeleteDraft(current.Id);

        Assert.That(result.IsFailure, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotDraft));
            Assert.That(timeline.Revisions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void DeleteDraft_WithUnknownId_ReturnsNotFoundError()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();

        Result result = timeline.DeleteDraft(Id(9));

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.NotFound));
    }

    [Test]
    public void CreateDraft_WithNullDefinition_ThrowsArgumentNullException()
    {
        RevisionTimeline<SyntheticLimitDefinition> timeline = Empty();

        Assert.That(
            () => timeline.CreateDraft(Id(1), null!, October(1), Author, null, null),
            Throws.ArgumentNullException);
    }
}
