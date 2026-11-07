namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record PriceObservationPageResponse(
    Guid InstrumentId,
    PriceObservationDto? Latest,
    IReadOnlyList<PriceObservationDto> Items,
    int PageSize,
    DateTimeOffset? Before,
    DateTimeOffset? NextBefore);
