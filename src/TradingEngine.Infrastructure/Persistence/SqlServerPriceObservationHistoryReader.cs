using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerPriceObservationHistoryReader : IPriceObservationHistoryReader
{
    private readonly TradingEngineDbContext _context;

    public SqlServerPriceObservationHistoryReader(TradingEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Result<PriceObservationHistory>> GetHistoryAsync(
        Guid instrumentId,
        Instant? before,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        bool exists = await _context.WatchedInstruments
            .AsNoTracking()
            .AnyAsync(
                instrument => instrument.Id == instrumentId && instrument.DeletedAt == null,
                cancellationToken);
        if (exists is false)
        {
            return PriceObservationErrors.InstrumentNotFound;
        }

        IQueryable<PriceObservationRow> scoped = _context.PriceObservations
            .AsNoTracking()
            .Where(row => row.WatchedInstrumentId == instrumentId);
        if (before is not null)
        {
            Instant cursor = before.Value;
            scoped = scoped.Where(row => row.ScheduledFor < cursor);
        }

        List<PriceObservationProjection> fetched = await Project(scoped
                .OrderByDescending(row => row.ScheduledFor)
                .Take(pageSize + 1))
            .ToListAsync(cancellationToken);

        bool hasOlder = fetched.Count > pageSize;
        PriceObservationRecord[] items = fetched
            .Take(pageSize)
            .Select(ToRecord)
            .ToArray();
        Instant? nextBefore = hasOlder ? items[^1].ScheduledFor : null;

        PriceObservationRecord? latest = before is null
            ? items.FirstOrDefault()
            : await GetLatestAsync(instrumentId, cancellationToken);

        return new PriceObservationHistory(
            instrumentId,
            latest,
            items,
            pageSize,
            before,
            nextBefore);
    }

    private async Task<PriceObservationRecord?> GetLatestAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        PriceObservationProjection? latest = await Project(_context.PriceObservations
                .AsNoTracking()
                .Where(row => row.WatchedInstrumentId == instrumentId)
                .OrderByDescending(row => row.ScheduledFor)
                .Take(1))
            .FirstOrDefaultAsync(cancellationToken);

        return latest is null ? null : ToRecord(latest);
    }

    private static IQueryable<PriceObservationProjection> Project(IQueryable<PriceObservationRow> rows)
    {
        return rows.Select(row => new PriceObservationProjection(
            row.ScheduledFor,
            row.RetrievedAt,
            row.ProviderObservedAt,
            row.Bid,
            row.Ask,
            row.QuoteCurrency,
            row.Provider,
            row.Timeliness));
    }

    private static PriceObservationRecord ToRecord(PriceObservationProjection row)
    {
        if (Enum.TryParse(row.Timeliness, ignoreCase: false, out MarketQuoteTimeliness timeliness) is false
            || Enum.IsDefined(timeliness) is false
            || int.TryParse(row.Timeliness, out int _))
        {
            throw new InvalidDataException(
                $"The stored observation timeliness '{row.Timeliness}' is not a defined value.");
        }

        return new PriceObservationRecord(
            row.ScheduledFor,
            row.RetrievedAt,
            row.ProviderObservedAt,
            row.Bid,
            row.Ask,
            row.QuoteCurrency,
            row.Provider,
            timeliness);
    }
}
