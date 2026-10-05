using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Domain.Tests.Revisions;

public sealed class SyntheticLimitPolicy
{
    public static readonly Error LimitNotPositive = Error.Validation(
        "synthetic_limit.limit_not_positive",
        "A synthetic limit must be greater than zero when committed.");

    private readonly RevisionTimeline<SyntheticLimitDefinition> _timeline =
        RevisionTimeline<SyntheticLimitDefinition>.Restore(
            Array.Empty<RestoredRevision<SyntheticLimitDefinition>>()).Value;

    public IReadOnlyList<Revision<SyntheticLimitDefinition>> Revisions => _timeline.Revisions;

    public IReadOnlyList<Revision<SyntheticLimitDefinition>> Drafts => _timeline.Drafts;

    public Revision<SyntheticLimitDefinition>? EffectiveAt(Instant instant)
    {
        return _timeline.EffectiveAt(instant);
    }

    public Result<Revision<SyntheticLimitDefinition>> CreateDraft(
        Guid draftId,
        SyntheticLimitDefinition definition,
        Instant createdAt)
    {
        return _timeline.CreateDraft(draftId, definition, createdAt, "synthetic-user", null, null);
    }

    public Result EditDraft(Guid draftId, SyntheticLimitDefinition definition)
    {
        return _timeline.EditDraft(draftId, definition, null, null);
    }

    public Result ApplyNow(Guid draftId, Instant now)
    {
        Error? failure = ValidateDefinition(draftId);
        if (failure is not null)
        {
            return failure;
        }

        return _timeline.ApplyNow(draftId, Guid.NewGuid(), now);
    }

    public Result Schedule(Guid draftId, Instant effectiveFrom, Instant now)
    {
        Error? failure = ValidateDefinition(draftId);
        if (failure is not null)
        {
            return failure;
        }

        return _timeline.Schedule(draftId, effectiveFrom, null, Guid.NewGuid(), now);
    }

    private Error? ValidateDefinition(Guid draftId)
    {
        Revision<SyntheticLimitDefinition>? draft = _timeline.Find(draftId);
        if (draft is not null && draft.Definition.Limit <= 0)
        {
            return LimitNotPositive;
        }

        return null;
    }
}
