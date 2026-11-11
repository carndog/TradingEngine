using NodaTime;

namespace TradingEngine.Application.Recommendations;

public sealed record EvaluationRunResult(
    Guid InvocationId,
    Instant StartedAt,
    Instant FinishedAt,
    bool DeadlineReached,
    IReadOnlyList<InstrumentEvaluationResult> Items);
