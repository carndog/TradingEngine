using NodaTime;

namespace TradingEngine.Application.Recommendations;

public sealed record RecommendationCommit(
    Guid InstrumentId,
    Instant ExpectedCheckpoint,
    Instant ProcessedSlot,
    Instant CommittedAt,
    TriggeredRecommendation? Intent);
