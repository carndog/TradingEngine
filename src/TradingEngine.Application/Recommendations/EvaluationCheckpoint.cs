using NodaTime;

namespace TradingEngine.Application.Recommendations;

public sealed record EvaluationCheckpoint(
    Guid InstrumentId,
    Instant LastProcessedScheduledFor);
