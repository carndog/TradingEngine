using NodaTime;

namespace TradingEngine.Application.PriceObservations;

public sealed record PriceObservationHistory(
    Guid InstrumentId,
    PriceObservationRecord? Latest,
    IReadOnlyList<PriceObservationRecord> Items,
    int PageSize,
    Instant? Before,
    Instant? NextBefore);
