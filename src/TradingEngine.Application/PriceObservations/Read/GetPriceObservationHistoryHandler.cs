using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.PriceObservations.Read;

public sealed class GetPriceObservationHistoryHandler
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;

    private readonly IPriceObservationHistoryReader _reader;

    public GetPriceObservationHistoryHandler(IPriceObservationHistoryReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<Result<PriceObservationHistory>> HandleAsync(
        GetPriceObservationHistory query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.PageSize is < 1 or > MaximumPageSize)
        {
            return PriceObservationErrors.PageSizeOutOfRange;
        }

        if (query.Before is not null && PersistedInstant.IsPersistable(query.Before.Value) is false)
        {
            return PriceObservationErrors.CursorInvalid;
        }

        return await _reader.GetHistoryAsync(
            query.InstrumentId,
            query.Before,
            query.PageSize,
            cancellationToken);
    }
}
