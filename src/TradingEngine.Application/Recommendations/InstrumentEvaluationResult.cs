using NodaTime;

namespace TradingEngine.Application.Recommendations;

public sealed record InstrumentEvaluationResult(
    Guid InstrumentId,
    int Evaluated,
    int NoAction,
    int SkippedNoRevision,
    int IntentsCreated,
    Instant? FailedSlot,
    string? FailureCode);
