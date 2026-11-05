using NodaTime;

namespace TradingEngine.Application.PriceCollection;

public sealed record PriceCollectionRunResult(
    Guid InvocationId,
    Instant StartedAt,
    Instant CompletedAt,
    bool DeadlineReached,
    IReadOnlyList<PriceCollectionItemResult> Items);
