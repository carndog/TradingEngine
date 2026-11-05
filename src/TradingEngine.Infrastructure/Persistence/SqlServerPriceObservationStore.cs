using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Time;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerPriceObservationStore : IPriceObservationStore
{
    private readonly TradingEngineDbContext _context;

    public SqlServerPriceObservationStore(TradingEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyDictionary<Guid, Instant>> GetLatestScheduledForAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);

        if (instrumentIds.Count == 0)
        {
            return new Dictionary<Guid, Instant>();
        }

        List<KeyValuePair<Guid, Instant>> latest = await _context.PriceObservations
            .AsNoTracking()
            .Where(row => instrumentIds.Contains(row.WatchedInstrumentId))
            .GroupBy(row => row.WatchedInstrumentId)
            .Select(group => new KeyValuePair<Guid, Instant>(
                group.Key,
                group.Max(row => row.ScheduledFor)))
            .ToListAsync(cancellationToken);

        return latest.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public async Task<bool> TryRecordObservationAsync(
        ScheduledPriceObservation observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (Enum.IsDefined(observation.Timeliness) is false)
        {
            throw new InvalidDataException(
                $"The quote timeliness '{observation.Timeliness}' is not a defined value.");
        }

        _context.PriceObservations.Add(new PriceObservationRow
        {
            WatchedInstrumentId = observation.InstrumentId,
            ScheduledFor = PersistedInstant.Require(
                observation.ScheduledFor,
                nameof(observation)),
            Provider = observation.Provider,
            ProviderInstrumentId = observation.ProviderInstrumentId,
            Bid = observation.Bid,
            Ask = observation.Ask,
            QuoteCurrency = observation.QuoteCurrency,
            ProviderObservedAt = PersistedInstant.Require(
                observation.ProviderObservedAt,
                nameof(observation)),
            RetrievedAt = PersistedInstant.Require(
                observation.RetrievedAt,
                nameof(observation)),
            Timeliness = observation.Timeliness.ToString(),
            ProviderRequestId = observation.ProviderRequestId,
            InvocationId = observation.InvocationId
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return false;
        }
    }

    public async Task RecordAttemptAsync(
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (Enum.IsDefined(attempt.Outcome) is false)
        {
            throw new InvalidDataException(
                $"The collection outcome '{attempt.Outcome}' is not a defined value.");
        }

        _context.PriceCollectionAttempts.Add(new PriceCollectionAttemptRow
        {
            WatchedInstrumentId = attempt.InstrumentId,
            ScheduledFor = PersistedInstant.Require(
                attempt.ScheduledFor,
                nameof(attempt)),
            Outcome = attempt.Outcome.ToString(),
            ErrorCode = attempt.ErrorCode,
            Provider = attempt.Provider,
            ProviderRequestId = attempt.ProviderRequestId,
            InvocationId = attempt.InvocationId,
            AttemptedAt = PersistedInstant.Require(
                attempt.AttemptedAt,
                nameof(attempt)),
            RecordedAt = PersistedInstant.Require(
                attempt.RecordedAt,
                nameof(attempt))
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
