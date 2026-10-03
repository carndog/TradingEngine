using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Domain.Tests.Revisions;

[TestFixture]
public sealed class RevisionTimelineRestoreTests
{
    private static readonly Instant October1 = Instant.FromUtc(2026, 10, 1, 0, 0);
    private static readonly Instant October5 = Instant.FromUtc(2026, 10, 5, 0, 0);
    private static readonly Instant October6 = Instant.FromUtc(2026, 10, 6, 0, 0);
    private static readonly Instant October10 = Instant.FromUtc(2026, 10, 10, 0, 0);
    private static readonly Instant October12 = Instant.FromUtc(2026, 10, 12, 0, 0);
    private const string Author = SyntheticTimelines.Author;

    [Test]
    public void Restore_WithCommittedAndDrafts_RebuildsCompleteTimeline()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, October5, "initial"),
                Committed(SyntheticTimelines.Id(2), 2, 110, October5, October5, October6, "raise"),
                Committed(SyntheticTimelines.Id(3), 3, 120, October6, October6, null, "raise again"),
                Draft(SyntheticTimelines.Id(4), 130, October10, "candidate", October10, null),
                Draft(SyntheticTimelines.Id(5), 140, October10, null, null, null)
            ]);

        Assert.That(result.IsSuccess, Is.True);
        RevisionTimeline<SyntheticLimitDefinition> timeline = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(timeline.Revisions, Has.Count.EqualTo(3));
            Assert.That(timeline.Drafts, Has.Count.EqualTo(2));
            Assert.That(
                timeline.Revisions.Select(revision => revision.Id),
                Is.EqualTo(new[] { SyntheticTimelines.Id(1), SyntheticTimelines.Id(2), SyntheticTimelines.Id(3) }));
            Assert.That(
                timeline.Revisions.Select(revision => revision.RevisionNumber),
                Is.EqualTo(new int?[] { 1, 2, 3 }));
            Assert.That(timeline.Revisions[0].EffectivePeriod!.EffectiveTo, Is.EqualTo(October5));
            Assert.That(timeline.Revisions[2].EffectivePeriod!.IsOpenEnded, Is.True);
            Assert.That(timeline.Drafts[0].Proposal, Is.EqualTo(new RevisionProposal(October10, null)));
            Assert.That(timeline.EffectiveAt(October5)!.Id, Is.EqualTo(SyntheticTimelines.Id(2)));
            Assert.That(timeline.EffectiveAt(October10)!.Id, Is.EqualTo(SyntheticTimelines.Id(3)));
            Assert.That(timeline.EffectiveAt(October1 - Duration.FromSeconds(1)), Is.Null);
        });
    }

    [Test]
    public void Restore_PreservesIdsAndMetadataWithoutReplayingOperations()
    {
        Instant createdAt = Instant.FromUtc(2026, 1, 1, 0, 0);
        SyntheticLimitDefinition definition = SyntheticTimelines.Limit(100);

        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                new RestoredRevision<SyntheticLimitDefinition>(
                    SyntheticTimelines.Id(1),
                    definition,
                    createdAt,
                    Author,
                    "recorded reason",
                    1,
                    October5,
                    null,
                    null,
                    null)
            ]);

        Assert.That(result.IsSuccess, Is.True);
        Revision<SyntheticLimitDefinition> revision = result.Value.Revisions[0];
        Assert.Multiple(() =>
        {
            Assert.That(revision.Id, Is.EqualTo(SyntheticTimelines.Id(1)));
            Assert.That(revision.CreatedAt, Is.EqualTo(createdAt));
            Assert.That(revision.CreatedBy, Is.EqualTo(Author));
            Assert.That(revision.ChangeReason, Is.EqualTo("recorded reason"));
            Assert.That(revision.Definition, Is.SameAs(definition));
            Assert.That(result.Value.Drafts, Is.Empty);
        });
    }

    [Test]
    public void Restore_WithEmptyList_ReturnsEmptyTimeline()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore([]);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Revisions, Is.Empty);
            Assert.That(result.Value.Drafts, Is.Empty);
        });
    }

    [Test]
    public void Restore_WithDuplicateRevisionIds_ReturnsDuplicateIdError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, null, null),
                Draft(SyntheticTimelines.Id(1), 110, October1, null, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.DuplicateId));
    }

    [Test]
    public void Restore_WithEmptyRevisionId_ReturnsIdRequiredError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(Guid.Empty, 1, 100, October1, October1, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.IdRequired));
    }

    [Test]
    public void Restore_WithMissingCreatedBy_ReturnsCreatedByRequiredError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                new RestoredRevision<SyntheticLimitDefinition>(
                    SyntheticTimelines.Id(1),
                    SyntheticTimelines.Limit(100),
                    October1,
                    " ",
                    null,
                    1,
                    October1,
                    null,
                    null,
                    null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.CreatedByRequired));
    }

    [Test]
    public void Restore_WithDraftCarryingNumber_ReturnsRestoredDraftInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                new RestoredRevision<SyntheticLimitDefinition>(
                    SyntheticTimelines.Id(1),
                    SyntheticTimelines.Limit(100),
                    October1,
                    Author,
                    null,
                    1,
                    null,
                    null,
                    null,
                    null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredDraftInvalid));
    }

    [Test]
    public void Restore_WithDraftCarryingEffectiveEnd_ReturnsRestoredDraftInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                new RestoredRevision<SyntheticLimitDefinition>(
                    SyntheticTimelines.Id(1),
                    SyntheticTimelines.Limit(100),
                    October1,
                    Author,
                    null,
                    null,
                    null,
                    October5,
                    null,
                    null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredDraftInvalid));
    }

    [Test]
    public void Restore_WithCommittedMissingNumber_ReturnsRestoredCommittedInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), null, 100, October1, October1, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredCommittedInvalid));
    }

    [Test]
    public void Restore_WithCommittedNonPositiveNumber_ReturnsRestoredCommittedInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 0, 100, October1, October1, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredCommittedInvalid));
    }

    [Test]
    public void Restore_WithCommittedCarryingProposal_ReturnsRestoredCommittedInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                new RestoredRevision<SyntheticLimitDefinition>(
                    SyntheticTimelines.Id(1),
                    SyntheticTimelines.Limit(100),
                    October1,
                    Author,
                    null,
                    1,
                    October1,
                    null,
                    October10,
                    null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredCommittedInvalid));
    }

    [Test]
    public void Restore_WithInvertedCommittedPeriod_ReturnsInvalidPeriodError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October5, October1, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.InvalidPeriod));
    }

    [Test]
    public void Restore_WithZeroLengthCommittedPeriod_ReturnsInvalidPeriodError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October5, October5, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.InvalidPeriod));
    }

    [Test]
    public void Restore_WithGapBetweenCommittedPeriods_ReturnsRestoredSequenceInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, October5, null),
                Committed(SyntheticTimelines.Id(2), 2, 110, October5, October6, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredSequenceInvalid));
    }

    [Test]
    public void Restore_WithOverlappingCommittedPeriods_ReturnsRestoredSequenceInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, October6, null),
                Committed(SyntheticTimelines.Id(2), 2, 110, October5, October5, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredSequenceInvalid));
    }

    [Test]
    public void Restore_WithNanosecondGapBetweenCommittedPeriods_ReturnsRestoredSequenceInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, October5, null),
                Committed(SyntheticTimelines.Id(2), 2, 110, October5, October5.PlusNanoseconds(1), null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredSequenceInvalid));
    }

    [Test]
    public void Restore_WithNanosecondOverlapBetweenCommittedPeriods_ReturnsRestoredSequenceInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, October5.PlusNanoseconds(1), null),
                Committed(SyntheticTimelines.Id(2), 2, 110, October5, October5, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredSequenceInvalid));
    }

    [Test]
    public void Restore_WithMinimumCommittedPeriod_PreservesExactBoundary()
    {
        Instant start = October5;
        Instant end = start.PlusTicks(1);

        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, start, null),
                Committed(SyntheticTimelines.Id(2), 2, 110, October1, start, end, null),
                Committed(SyntheticTimelines.Id(3), 3, 120, October1, end, null, null)
            ]);

        Assert.That(result.IsSuccess, Is.True);
        RevisionTimeline<SyntheticLimitDefinition> timeline = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(timeline.EffectiveAt(start - Duration.FromNanoseconds(1))!.Id, Is.EqualTo(SyntheticTimelines.Id(1)));
            Assert.That(timeline.EffectiveAt(start)!.Id, Is.EqualTo(SyntheticTimelines.Id(2)));
            Assert.That(timeline.EffectiveAt(start + Duration.FromNanoseconds(50))!.Id, Is.EqualTo(SyntheticTimelines.Id(2)));
            Assert.That(timeline.EffectiveAt(end - Duration.FromNanoseconds(1))!.Id, Is.EqualTo(SyntheticTimelines.Id(2)));
            Assert.That(timeline.EffectiveAt(end)!.Id, Is.EqualTo(SyntheticTimelines.Id(3)));
        });
    }

    [Test]
    public void Restore_WithDuplicateCommittedStarts_ReturnsRestoredSequenceInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October5, null, null),
                Committed(SyntheticTimelines.Id(2), 2, 110, October5, October5, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredSequenceInvalid));
    }

    [Test]
    public void Restore_WithBoundedLastCommitted_ReturnsRestoredSequenceInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, October5, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredSequenceInvalid));
    }

    [Test]
    public void Restore_WithNumberNotMatchingPosition_ReturnsRestoredSequenceInvalidError()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, October5, null),
                Committed(SyntheticTimelines.Id(2), 5, 110, October5, October5, null, null)
            ]);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Is.EqualTo(RevisionErrors.RestoredSequenceInvalid));
    }

    [Test]
    public void Restore_WithMultipleOverlappingDraftProposals_RestoresWithoutOrderingRules()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> result = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, null, null),
                Draft(SyntheticTimelines.Id(2), 110, October5, null, October6, October10),
                Draft(SyntheticTimelines.Id(3), 120, October5, null, October5, October6),
                Draft(SyntheticTimelines.Id(4), 130, October5, null, null, null)
            ]);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Drafts, Has.Count.EqualTo(3));
            Assert.That(
                result.Value.Drafts[0].Proposal,
                Is.EqualTo(new RevisionProposal(October6, October10)));
            Assert.That(
                result.Value.Drafts[1].Proposal,
                Is.EqualTo(new RevisionProposal(October5, October6)));
            Assert.That(result.Value.Drafts[2].Proposal, Is.Null);
            Assert.That(result.Value.EffectiveAt(October6)!.Id, Is.EqualTo(SyntheticTimelines.Id(1)));
        });
    }

    [Test]
    public void Restore_ThenDomainOperations_ApplyNormalRules()
    {
        Result<RevisionTimeline<SyntheticLimitDefinition>> restored = RevisionTimeline<SyntheticLimitDefinition>
            .Restore(
            [
                Committed(SyntheticTimelines.Id(1), 1, 100, October1, October1, October10, null),
                Committed(SyntheticTimelines.Id(2), 2, 110, October5, October10, null, null)
            ]);

        Assert.That(restored.IsSuccess, Is.True);
        RevisionTimeline<SyntheticLimitDefinition> timeline = restored.Value;

        Result edited = timeline.EditScheduledRevision(
            SyntheticTimelines.Id(2),
            SyntheticTimelines.Limit(115),
            "adjust",
            October5);
        Result begun = timeline.EditScheduledRevision(
            SyntheticTimelines.Id(1),
            SyntheticTimelines.Limit(105),
            null,
            October5);
        Result<Revision<SyntheticLimitDefinition>> draft = timeline.CreateDraft(
            SyntheticTimelines.Id(3),
            SyntheticTimelines.Limit(120),
            October5,
            Author,
            null,
            null);
        Result scheduled = timeline.Schedule(draft.Value.Id, October12, October5);

        Assert.Multiple(() =>
        {
            Assert.That(edited.IsSuccess, Is.True);
            Assert.That(begun.Error, Is.EqualTo(RevisionErrors.PeriodBegun));
            Assert.That(scheduled.IsSuccess, Is.True);
            Assert.That(timeline.EffectiveAt(October12)!.Id, Is.EqualTo(SyntheticTimelines.Id(3)));
            Assert.That(
                timeline.Revisions.Select(revision => revision.RevisionNumber),
                Is.EqualTo(new int?[] { 1, 2, 3 }));
        });
    }

    private static RestoredRevision<SyntheticLimitDefinition> Committed(
        Guid id,
        int? number,
        int limit,
        Instant createdAt,
        Instant effectiveFrom,
        Instant? effectiveTo,
        string? changeReason)
    {
        return new RestoredRevision<SyntheticLimitDefinition>(
            id,
            SyntheticTimelines.Limit(limit),
            createdAt,
            Author,
            changeReason,
            number,
            effectiveFrom,
            effectiveTo,
            null,
            null);
    }

    private static RestoredRevision<SyntheticLimitDefinition> Draft(
        Guid id,
        int limit,
        Instant createdAt,
        string? changeReason,
        Instant? proposedFrom,
        Instant? proposedTo)
    {
        return new RestoredRevision<SyntheticLimitDefinition>(
            id,
            SyntheticTimelines.Limit(limit),
            createdAt,
            Author,
            changeReason,
            null,
            null,
            null,
            proposedFrom,
            proposedTo);
    }
}
