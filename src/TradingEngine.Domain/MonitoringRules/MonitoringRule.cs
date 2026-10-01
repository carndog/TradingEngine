using NodaTime;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Domain.MonitoringRules;

public sealed class MonitoringRule
{
    private readonly RevisionTimeline<ChartAnalysisDefinition> _timeline = new();

    private MonitoringRule(Guid id, Guid watchedInstrumentId, Instant createdAt)
    {
        Id = id;
        WatchedInstrumentId = watchedInstrumentId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid WatchedInstrumentId { get; }

    public Instant CreatedAt { get; }

    public IReadOnlyList<Revision<ChartAnalysisDefinition>> Revisions => _timeline.Revisions;

    public IReadOnlyList<Revision<ChartAnalysisDefinition>> Drafts => _timeline.Drafts;

    public static Result<MonitoringRule> Create(Guid id, Guid watchedInstrumentId, Instant createdAt)
    {
        if (id == Guid.Empty)
        {
            return MonitoringRuleErrors.IdRequired;
        }

        if (watchedInstrumentId == Guid.Empty)
        {
            return MonitoringRuleErrors.WatchedInstrumentIdRequired;
        }

        return new MonitoringRule(id, watchedInstrumentId, createdAt);
    }

    public Revision<ChartAnalysisDefinition>? EffectiveAt(Instant instant)
    {
        return _timeline.EffectiveAt(instant);
    }

    public Revision<ChartAnalysisDefinition>? FindRevision(Guid revisionId)
    {
        return _timeline.Find(revisionId);
    }

    public Result<Revision<ChartAnalysisDefinition>> CreateDraft(
        Guid draftId,
        ChartAnalysisDefinition definition,
        Instant createdAt,
        string? createdBy,
        string? changeReason,
        RevisionProposal? proposal)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return _timeline.CreateDraft(draftId, definition, createdAt, createdBy, changeReason, proposal);
    }

    public Result EditDraft(
        Guid draftId,
        ChartAnalysisDefinition definition,
        string? changeReason,
        RevisionProposal? proposal)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return _timeline.EditDraft(draftId, definition, changeReason, proposal);
    }

    public Result DeleteDraft(Guid draftId)
    {
        return _timeline.DeleteDraft(draftId);
    }

    public Result ApplyNow(Guid draftId, Instant now)
    {
        return _timeline.ApplyNow(draftId, now);
    }

    public Result Schedule(Guid draftId, Instant effectiveFrom, Instant now)
    {
        return _timeline.Schedule(draftId, effectiveFrom, now);
    }

    public Result EditScheduledRevision(
        Guid revisionId,
        ChartAnalysisDefinition definition,
        string? changeReason,
        Instant now)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return _timeline.EditScheduledRevision(revisionId, definition, changeReason, now);
    }

    public Result Reschedule(Guid revisionId, Instant effectiveFrom, Instant now)
    {
        return _timeline.Reschedule(revisionId, effectiveFrom, now);
    }

    public Result RemoveScheduledRevision(Guid revisionId, Instant now)
    {
        return _timeline.RemoveScheduledRevision(revisionId, now);
    }
}
