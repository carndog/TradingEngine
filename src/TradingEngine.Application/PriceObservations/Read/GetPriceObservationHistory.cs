using NodaTime;

namespace TradingEngine.Application.PriceObservations.Read;

public sealed record GetPriceObservationHistory(Guid InstrumentId, Instant? Before, int PageSize);
