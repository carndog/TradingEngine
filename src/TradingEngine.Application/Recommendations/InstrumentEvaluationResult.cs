using NodaTime;

namespace TradingEngine.Application.Recommendations;

public sealed record InstrumentEvaluationResult(
    Guid InstrumentId,
    int Evaluated,
    int NoAction,
    int SkippedNoRevision,
    IReadOnlyList<CommittedIntent> CommittedIntents,
    Instant? FailedSlot,
    string? FailureCode)
{
    public int IntentsCreated => CommittedIntents.Count;
}
