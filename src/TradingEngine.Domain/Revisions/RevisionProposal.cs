using NodaTime;

namespace TradingEngine.Domain.Revisions;

public sealed record RevisionProposal(Instant? EffectiveFrom, Instant? EffectiveTo);
