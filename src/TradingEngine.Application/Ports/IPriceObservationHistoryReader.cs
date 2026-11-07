using NodaTime;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface IPriceObservationHistoryReader
{
    Task<Result<PriceObservationHistory>> GetHistoryAsync(
        Guid instrumentId,
        Instant? before,
        int pageSize,
        CancellationToken cancellationToken);
}
