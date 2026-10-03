using NodaTime;

namespace TradingEngine.Domain.Revisions;

public sealed record RestoredRevision<TDefinition>(
    Guid Id,
    TDefinition Definition,
    Instant CreatedAt,
    string CreatedBy,
    string? ChangeReason,
    int? RevisionNumber,
    Instant? EffectiveFrom,
    Instant? EffectiveTo,
    Instant? ProposedFrom,
    Instant? ProposedTo)
    where TDefinition : class;
