using NodaTime;

namespace TradingEngine.Application.PriceCollection;

public sealed record PriceCollectionAttempt(
    Guid InstrumentId,
    Instant ScheduledFor,
    PriceCollectionOutcome Outcome,
    string? ErrorCode,
    string? Provider,
    string? ProviderRequestId,
    Guid InvocationId,
    Instant AttemptedAt,
    Instant RecordedAt);
