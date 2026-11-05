using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.Time;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerPriceObservationStore : IPriceObservationStore
{
    private const int MaximumNativeScale = 18;
    private const string SlotIndexName =
        "UX_PriceObservations_WatchedInstrumentId_ScheduledFor";

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

    public async Task<IReadOnlyDictionary<Guid, PriceCollectionAttempt>> GetLatestAttemptsAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);

        if (instrumentIds.Count == 0)
        {
            return new Dictionary<Guid, PriceCollectionAttempt>();
        }

        List<PriceCollectionAttemptRow> rows = await _context.PriceCollectionAttempts
            .AsNoTracking()
            .Where(row =>
                instrumentIds.Contains(row.WatchedInstrumentId)
                && _context.PriceCollectionAttempts
                    .Where(candidate =>
                        candidate.WatchedInstrumentId == row.WatchedInstrumentId)
                    .Max(candidate => (Instant?)candidate.RecordedAt) == row.RecordedAt)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.WatchedInstrumentId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.ScheduledFor)
                    .Select(MapAttempt)
                    .First());
    }

    public async Task<bool> TryRecordObservationAndAttemptAsync(
        ScheduledPriceObservation observation,
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(attempt);

        if (Enum.IsDefined(observation.Timeliness) is false)
        {
            throw new InvalidDataException(
                $"The quote timeliness '{observation.Timeliness}' is not a defined value.");
        }

        RequirePersistableScale(observation.Bid, nameof(observation.Bid));
        RequirePersistableScale(observation.Ask, nameof(observation.Ask));

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
        _context.PriceCollectionAttempts.Add(MapAttemptRow(attempt));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            _context.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateException exception)
        {
            _context.ChangeTracker.Clear();
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The observation save was cancelled.",
                    exception,
                    cancellationToken);
            }

            if (IsUniqueViolation(exception))
            {
                return false;
            }

            throw new PriceObservationStorageException(
                "Persisting the observation and its collection attempt failed.",
                exception);
        }
    }

    public async Task RecordAttemptAsync(
        PriceCollectionAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        _context.PriceCollectionAttempts.Add(MapAttemptRow(attempt));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _context.ChangeTracker.Clear();
            throw;
        }
        catch (DbUpdateException exception)
        {
            _context.ChangeTracker.Clear();
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The attempt save was cancelled.",
                    exception,
                    cancellationToken);
            }

            throw new PriceObservationStorageException(
                "Persisting the collection attempt failed.",
                exception);
        }
    }

    private static PriceCollectionAttemptRow MapAttemptRow(PriceCollectionAttempt attempt)
    {
        if (Enum.IsDefined(attempt.Outcome) is false)
        {
            throw new InvalidDataException(
                $"The collection outcome '{attempt.Outcome}' is not a defined value.");
        }

        return new PriceCollectionAttemptRow
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
        };
    }

    private static PriceCollectionAttempt MapAttempt(PriceCollectionAttemptRow row)
    {
        return new PriceCollectionAttempt(
            row.WatchedInstrumentId,
            row.ScheduledFor,
            Enum.Parse<PriceCollectionOutcome>(row.Outcome),
            row.ErrorCode,
            row.Provider,
            row.ProviderRequestId,
            row.InvocationId,
            row.AttemptedAt,
            row.RecordedAt);
    }

    private static void RequirePersistableScale(decimal? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        int scale = (decimal.GetBits(value.Value)[3] >> 16) & 0xFF;
        if (scale > MaximumNativeScale)
        {
            throw new InvalidDataException(
                $"The {parameterName} value has scale {scale}, finer than " +
                $"decimal(38,{MaximumNativeScale}) can store without rounding.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException
        {
            Number: 2601 or 2627
        } sqlException
            && sqlException.Message.Contains(
                SlotIndexName,
                StringComparison.Ordinal);
    }
}
