using NodaTime;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Revisions;

public sealed class RevisionTimeline<TDefinition>
    where TDefinition : class
{
    private readonly List<Revision<TDefinition>> _committed = [];
    private readonly List<Revision<TDefinition>> _drafts = [];

    public IReadOnlyList<Revision<TDefinition>> Revisions => _committed.ToArray();

    public IReadOnlyList<Revision<TDefinition>> Drafts => _drafts.ToArray();

    public Revision<TDefinition>? EffectiveAt(Instant instant)
    {
        return _committed.FirstOrDefault(revision => revision.IsEffectiveAt(instant));
    }

    public Revision<TDefinition>? Find(Guid revisionId)
    {
        return _committed.Concat(_drafts).FirstOrDefault(revision => revision.Id == revisionId);
    }

    public static Result<RevisionTimeline<TDefinition>> Restore(
        IReadOnlyList<RestoredRevision<TDefinition>> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);

        RevisionTimeline<TDefinition> timeline = new();
        HashSet<Guid> seen = [];

        foreach (RestoredRevision<TDefinition> revision in revisions)
        {
            ArgumentNullException.ThrowIfNull(revision);
            ArgumentNullException.ThrowIfNull(revision.Definition);

            if (revision.Id == Guid.Empty)
            {
                return RevisionErrors.IdRequired;
            }

            if (string.IsNullOrWhiteSpace(revision.CreatedBy))
            {
                return RevisionErrors.CreatedByRequired;
            }

            if (seen.Add(revision.Id) is false)
            {
                return RevisionErrors.DuplicateId;
            }

            Result<Revision<TDefinition>> restored = RestoreRevision(revision);
            if (restored.IsFailure)
            {
                return restored.Error;
            }

            if (restored.Value.IsDraft)
            {
                timeline._drafts.Add(restored.Value);
            }
            else
            {
                timeline._committed.Add(restored.Value);
            }
        }

        Result sequence = timeline.ValidateRestoredSequence();
        if (sequence.IsFailure)
        {
            return sequence.Error;
        }

        return timeline;
    }

    private static Result<Revision<TDefinition>> RestoreRevision(
        RestoredRevision<TDefinition> revision)
    {
        RevisionProposal? proposal = revision.ProposedFrom is null && revision.ProposedTo is null
            ? null
            : new RevisionProposal(revision.ProposedFrom, revision.ProposedTo);

        if (revision.EffectiveFrom is null)
        {
            if (revision.EffectiveTo is not null || revision.RevisionNumber is not null)
            {
                return RevisionErrors.RestoredDraftInvalid;
            }

            return Revision<TDefinition>.Restore(
                revision.Id,
                revision.Definition,
                revision.CreatedAt,
                revision.CreatedBy,
                revision.ChangeReason,
                null,
                null,
                proposal);
        }

        if (revision.RevisionNumber is null || revision.RevisionNumber <= 0)
        {
            return RevisionErrors.RestoredCommittedInvalid;
        }

        if (proposal is not null)
        {
            return RevisionErrors.RestoredCommittedInvalid;
        }

        Result<EffectivePeriod> period = EffectivePeriod.Create(
            revision.EffectiveFrom.Value,
            revision.EffectiveTo);
        if (period.IsFailure)
        {
            return period.Error;
        }

        return Revision<TDefinition>.Restore(
            revision.Id,
            revision.Definition,
            revision.CreatedAt,
            revision.CreatedBy,
            revision.ChangeReason,
            revision.RevisionNumber,
            period.Value,
            null);
    }

    private Result ValidateRestoredSequence()
    {
        _committed.Sort((left, right) =>
            left.EffectivePeriod!.EffectiveFrom.CompareTo(right.EffectivePeriod!.EffectiveFrom));

        for (int index = 0; index < _committed.Count; index++)
        {
            Revision<TDefinition> revision = _committed[index];
            bool ordered = index == 0
                || _committed[index - 1].EffectivePeriod!.EffectiveFrom
                    < revision.EffectivePeriod!.EffectiveFrom;
            if (ordered is false)
            {
                return RevisionErrors.RestoredSequenceInvalid;
            }

            if (revision.RevisionNumber != index + 1)
            {
                return RevisionErrors.RestoredSequenceInvalid;
            }

            Instant? expectedEnd = index + 1 < _committed.Count
                ? _committed[index + 1].EffectivePeriod!.EffectiveFrom
                : null;
            if (revision.EffectivePeriod!.EffectiveTo != expectedEnd)
            {
                return RevisionErrors.RestoredSequenceInvalid;
            }
        }

        return Result.Success();
    }

    public Result<Revision<TDefinition>> CreateDraft(
        Guid draftId,
        TDefinition definition,
        Instant createdAt,
        string? createdBy,
        string? changeReason,
        RevisionProposal? proposal)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (draftId == Guid.Empty)
        {
            return RevisionErrors.IdRequired;
        }

        if (string.IsNullOrWhiteSpace(createdBy))
        {
            return RevisionErrors.CreatedByRequired;
        }

        if (Find(draftId) is not null)
        {
            return RevisionErrors.DuplicateId;
        }

        Revision<TDefinition> draft = Revision<TDefinition>.CreateDraft(
            draftId,
            definition,
            createdAt,
            createdBy,
            changeReason,
            proposal);
        _drafts.Add(draft);

        return draft;
    }

    public Result EditDraft(
        Guid draftId,
        TDefinition definition,
        string? changeReason,
        RevisionProposal? proposal)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Result<Revision<TDefinition>> draft = FindDraft(draftId);
        if (draft.IsFailure)
        {
            return draft.Error;
        }

        draft.Value.Replace(definition, changeReason);
        draft.Value.Propose(proposal);

        return Result.Success();
    }

    public Result DeleteDraft(Guid draftId)
    {
        Result<Revision<TDefinition>> draft = FindDraft(draftId);
        if (draft.IsFailure)
        {
            return draft.Error;
        }

        _drafts.Remove(draft.Value);

        return Result.Success();
    }

    public Result ApplyNow(Guid draftId, Instant now)
    {
        return Schedule(draftId, now, now);
    }

    public Result Schedule(Guid draftId, Instant effectiveFrom, Instant now)
    {
        Result<Revision<TDefinition>> draft = FindDraft(draftId);
        if (draft.IsFailure)
        {
            return draft.Error;
        }

        if (effectiveFrom < now)
        {
            return RevisionErrors.Backdated;
        }

        Revision<TDefinition>? occupant = EffectiveAt(effectiveFrom);
        if (occupant is not null && occupant.EffectivePeriod!.EffectiveFrom == effectiveFrom)
        {
            return RevisionErrors.StartConflict;
        }

        Instant? effectiveTo = occupant is not null
            ? occupant.EffectivePeriod!.EffectiveTo
            : NextStartAfter(effectiveFrom);
        Result<EffectivePeriod> period = EffectivePeriod.Create(effectiveFrom, effectiveTo);
        if (period.IsFailure)
        {
            return period.Error;
        }

        Result<EffectivePeriod>? closingPeriod = occupant is null
            ? null
            : EffectivePeriod.Create(occupant.EffectivePeriod!.EffectiveFrom, effectiveFrom);
        if (closingPeriod is { IsFailure: true })
        {
            return closingPeriod.Error;
        }

        occupant?.ChangePeriod(closingPeriod!.Value);
        draft.Value.Commit(period.Value);
        _drafts.Remove(draft.Value);
        _committed.Add(draft.Value);
        Reorder();

        return Result.Success();
    }

    public Result EditScheduledRevision(
        Guid revisionId,
        TDefinition definition,
        string? changeReason,
        Instant now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Result<Revision<TDefinition>> revision = FindScheduledRevision(revisionId, now);
        if (revision.IsFailure)
        {
            return revision.Error;
        }

        revision.Value.Replace(definition, changeReason);

        return Result.Success();
    }

    public Result Reschedule(Guid revisionId, Instant effectiveFrom, Instant now)
    {
        Result<Revision<TDefinition>> found = FindScheduledRevision(revisionId, now);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (effectiveFrom < now)
        {
            return RevisionErrors.Backdated;
        }

        Revision<TDefinition> revision = found.Value;
        EffectivePeriod current = revision.EffectivePeriod!;
        Result<EffectivePeriod> period = EffectivePeriod.Create(effectiveFrom, current.EffectiveTo);
        if (period.IsFailure)
        {
            return period.Error;
        }

        Revision<TDefinition>? predecessor = AdjacentPredecessor(revision);
        Result<EffectivePeriod>? predecessorPeriod = predecessor is null
            ? null
            : EffectivePeriod.Create(predecessor.EffectivePeriod!.EffectiveFrom, effectiveFrom);
        if (predecessorPeriod is { IsFailure: true })
        {
            return predecessorPeriod.Error;
        }

        predecessor?.ChangePeriod(predecessorPeriod!.Value);
        revision.ChangePeriod(period.Value);
        Reorder();

        return Result.Success();
    }

    public Result RemoveScheduledRevision(Guid revisionId, Instant now)
    {
        Result<Revision<TDefinition>> found = FindScheduledRevision(revisionId, now);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Revision<TDefinition> revision = found.Value;
        Revision<TDefinition>? predecessor = AdjacentPredecessor(revision);
        Result<EffectivePeriod>? predecessorPeriod = predecessor is null
            ? null
            : EffectivePeriod.Create(
                predecessor.EffectivePeriod!.EffectiveFrom,
                revision.EffectivePeriod!.EffectiveTo);
        if (predecessorPeriod is { IsFailure: true })
        {
            return predecessorPeriod.Error;
        }

        predecessor?.ChangePeriod(predecessorPeriod!.Value);
        _committed.Remove(revision);
        Reorder();

        return Result.Success();
    }

    private Result<Revision<TDefinition>> FindDraft(Guid draftId)
    {
        Revision<TDefinition>? revision = Find(draftId);
        if (revision is null)
        {
            return RevisionErrors.NotFound;
        }

        if (revision.IsDraft is false)
        {
            return RevisionErrors.NotDraft;
        }

        return revision;
    }

    private Result<Revision<TDefinition>> FindScheduledRevision(Guid revisionId, Instant now)
    {
        Revision<TDefinition>? revision = Find(revisionId);
        if (revision is null)
        {
            return RevisionErrors.NotFound;
        }

        if (revision.IsDraft)
        {
            return RevisionErrors.NotCommitted;
        }

        if (revision.HasBegun(now))
        {
            return RevisionErrors.PeriodBegun;
        }

        return revision;
    }

    private Instant? NextStartAfter(Instant instant)
    {
        Revision<TDefinition>? next = _committed
            .Where(revision => revision.EffectivePeriod!.EffectiveFrom > instant)
            .MinBy(revision => revision.EffectivePeriod!.EffectiveFrom);

        return next?.EffectivePeriod!.EffectiveFrom;
    }

    private Revision<TDefinition>? AdjacentPredecessor(Revision<TDefinition> revision)
    {
        Instant start = revision.EffectivePeriod!.EffectiveFrom;

        return _committed.FirstOrDefault(candidate =>
            candidate != revision && candidate.EffectivePeriod!.EffectiveTo == start);
    }

    private void Reorder()
    {
        _committed.Sort((left, right) =>
            left.EffectivePeriod!.EffectiveFrom.CompareTo(right.EffectivePeriod!.EffectiveFrom));

        for (int index = 0; index < _committed.Count; index++)
        {
            _committed[index].Number(index + 1);
        }
    }
}
