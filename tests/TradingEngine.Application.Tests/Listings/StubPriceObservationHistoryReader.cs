using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.Listings;

internal sealed class StubPriceObservationHistoryReader : IPriceObservationHistoryReader
{
    public Result<PriceObservationHistory> Result { get; set; } =
        PriceObservationErrors.InstrumentNotFound;

    public Task<Result<PriceObservationHistory>> GetHistoryAsync(
        Guid instrumentId,
        Instant? before,
        int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result);
    }
}
