using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Tests.PriceObservations;

internal sealed class RecordingPriceObservationHistoryReader : IPriceObservationHistoryReader
{
    public int CallCount { get; private set; }

    public Guid? LastInstrumentId { get; private set; }

    public Instant? LastBefore { get; private set; }

    public int? LastPageSize { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public Result<PriceObservationHistory> Result { get; set; } =
        PriceObservationErrors.InstrumentNotFound;

    public Task<Result<PriceObservationHistory>> GetHistoryAsync(
        Guid instrumentId,
        Instant? before,
        int pageSize,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastInstrumentId = instrumentId;
        LastBefore = before;
        LastPageSize = pageSize;
        LastCancellationToken = cancellationToken;
        return Task.FromResult(Result);
    }
}
