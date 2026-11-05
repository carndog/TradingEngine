using NodaTime;

namespace TradingEngine.Application.PriceCollection;

public sealed record PriceCollectionItemResult(
    Guid InstrumentId,
    Instant ScheduledFor,
    PriceCollectionOutcome Outcome,
    string? ErrorCode,
    string? ProviderRequestId);
