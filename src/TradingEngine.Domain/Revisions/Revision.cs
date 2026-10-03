using NodaTime;

namespace TradingEngine.Domain.Revisions;

public sealed class Revision<TDefinition>
    where TDefinition : class
{
    private Revision(
        Guid id,
        TDefinition definition,
        Instant createdAt,
        string createdBy,
        string? changeReason,
        RevisionProposal? proposal)
    {
        Id = id;
        Definition = definition;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ChangeReason = changeReason;
        Proposal = proposal;
    }

    public Guid Id { get; }

    public int? RevisionNumber { get; private set; }

    public Instant CreatedAt { get; }

    public string CreatedBy { get; }

    public string? ChangeReason { get; private set; }

    public TDefinition Definition { get; private set; }

    public EffectivePeriod? EffectivePeriod { get; private set; }

    public RevisionProposal? Proposal { get; private set; }

    public bool IsDraft => EffectivePeriod is null;

    public bool HasBegun(Instant now)
    {
        return EffectivePeriod is not null && EffectivePeriod.HasBegun(now);
    }

    public bool IsEffectiveAt(Instant instant)
    {
        return EffectivePeriod is not null && EffectivePeriod.Contains(instant);
    }

    internal static Revision<TDefinition> CreateDraft(
        Guid id,
        TDefinition definition,
        Instant createdAt,
        string createdBy,
        string? changeReason,
        RevisionProposal? proposal)
    {
        return new Revision<TDefinition>(id, definition, createdAt, createdBy, changeReason, proposal);
    }

    internal static Revision<TDefinition> Restore(
        Guid id,
        TDefinition definition,
        Instant createdAt,
        string createdBy,
        string? changeReason,
        int? revisionNumber,
        EffectivePeriod? period,
        RevisionProposal? proposal)
    {
        Revision<TDefinition> revision = new(id, definition, createdAt, createdBy, changeReason, proposal)
        {
            EffectivePeriod = period,
            RevisionNumber = revisionNumber
        };

        return revision;
    }

    internal void Replace(TDefinition definition, string? changeReason)
    {
        Definition = definition;
        ChangeReason = changeReason;
    }

    internal void Propose(RevisionProposal? proposal)
    {
        Proposal = proposal;
    }

    internal void Commit(EffectivePeriod period)
    {
        EffectivePeriod = period;
        Proposal = null;
    }

    internal void ChangePeriod(EffectivePeriod period)
    {
        EffectivePeriod = period;
    }

    internal void Number(int revisionNumber)
    {
        RevisionNumber = revisionNumber;
    }
}
