using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.PriceObservations;

internal sealed class StubPriceObservationHistoryReader : IPriceObservationHistoryReader
{
    public Result<PriceObservationHistory> Result { get; set; } =
        PriceObservationErrors.InstrumentNotFound;

    public int CallCount { get; private set; }

    public Instant? LastBefore { get; private set; }

    public int? LastPageSize { get; private set; }

    public bool LastTokenCanBeCancelled { get; private set; }

    public Task<Result<PriceObservationHistory>> GetHistoryAsync(
        Guid instrumentId,
        Instant? before,
        int pageSize,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastBefore = before;
        LastPageSize = pageSize;
        LastTokenCanBeCancelled = cancellationToken.CanBeCanceled;
        return Task.FromResult(Result);
    }
}
