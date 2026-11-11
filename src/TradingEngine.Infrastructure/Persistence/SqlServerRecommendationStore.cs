using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Recommendations;
using TradingEngine.Application.Time;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerRecommendationStore : IRecommendationEvaluationStore
{
    private const string IntentIndexName =
        "UX_NotificationIntents_WatchedInstrumentId_ScheduledFor";

    private readonly TradingEngineDbContext _context;

    public SqlServerRecommendationStore(TradingEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<EvaluableInstrument>> ListEvaluableAsync(
        CancellationToken cancellationToken)
    {
        List<EvaluableInstrument> instruments = await _context.WatchedInstruments
            .AsNoTracking()
            .Where(instrument =>
                instrument.DeletedAt == null
                && _context.MonitoringRules.Any(
                    rule => rule.WatchedInstrumentId == instrument.Id))
            .OrderBy(instrument => instrument.Symbol)
            .ThenBy(instrument => instrument.Exchange)
            .ThenBy(instrument => instrument.QuoteCurrency)
            .ThenBy(instrument => instrument.Id)
            .Select(instrument => new EvaluableInstrument(
                instrument.Id,
                instrument.Symbol,
                instrument.Exchange,
                instrument.QuoteCurrency))
            .ToListAsync(cancellationToken);

        return instruments;
    }

    public async Task<EvaluationCheckpoint?> GetCheckpointAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        EvaluationCheckpointRow? row = await _context.RecommendationCheckpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(
                checkpoint => checkpoint.WatchedInstrumentId == instrumentId,
                cancellationToken);

        return row is null
            ? null
            : new EvaluationCheckpoint(row.WatchedInstrumentId, row.LastProcessedScheduledFor);
    }

    public async Task<bool> TrySeedCheckpointAsync(
        Guid instrumentId,
        Instant establishedAt,
        CancellationToken cancellationToken)
    {
        Instant persisted = PersistedInstant.Require(
            establishedAt,
            nameof(establishedAt));

        _context.RecommendationCheckpoints.Add(new EvaluationCheckpointRow
        {
            WatchedInstrumentId = instrumentId,
            LastProcessedScheduledFor = persisted,
            EstablishedAt = persisted,
            UpdatedAt = persisted
        });

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
                    "The checkpoint seed was cancelled.",
                    exception,
                    cancellationToken);
            }

            if (exception.InnerException is SqlException { Number: 2601 or 2627 or 547 })
            {
                return false;
            }

            throw new RecommendationStorageException(
                "Persisting the evaluation checkpoint failed.",
                exception);
        }
    }

    public async Task<SavedPriceObservation?> GetObservationAtAsync(
        Guid instrumentId,
        Instant scheduledFor,
        CancellationToken cancellationToken)
    {
        PriceObservationRow? row = await _context.PriceObservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                observation =>
                    observation.WatchedInstrumentId == instrumentId
                    && observation.ScheduledFor == scheduledFor,
                cancellationToken);

        return row is null ? null : MapObservation(row);
    }

    public async Task<SavedPriceObservation?> GetLatestObservationAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        PriceObservationRow? row = await _context.PriceObservations
            .AsNoTracking()
            .Where(observation => observation.WatchedInstrumentId == instrumentId)
            .OrderByDescending(observation => observation.ScheduledFor)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : MapObservation(row);
    }

    public async Task<IReadOnlyList<SavedPriceObservation>> ListObservationsAfterAsync(
        Guid instrumentId,
        Instant afterScheduledFor,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        List<SavedPriceObservation> observations = await _context.PriceObservations
            .AsNoTracking()
            .Where(observation =>
                observation.WatchedInstrumentId == instrumentId
                && observation.ScheduledFor > afterScheduledFor)
            .OrderBy(observation => observation.ScheduledFor)
            .Take(maxCount)
            .Select(observation => new SavedPriceObservation(
                observation.ScheduledFor,
                observation.Bid,
                observation.Ask,
                observation.QuoteCurrency))
            .ToListAsync(cancellationToken);

        return observations;
    }

    public async Task<EvaluationCommitOutcome> CommitEvaluationAsync(
        RecommendationCommit commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        Validate(commit);

        DateTime processedSlot = commit.ProcessedSlot.ToDateTimeUtc();
        DateTime expected = commit.ExpectedCheckpoint.ToDateTimeUtc();
        DateTime committedAt = commit.CommittedAt.ToDateTimeUtc();

        try
        {
            await using IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync(cancellationToken);

            int updated = await _context.Database.ExecuteSqlAsync(
                $"""
                UPDATE RecommendationCheckpoints
                SET LastProcessedScheduledFor = {processedSlot},
                    UpdatedAt = {committedAt}
                WHERE WatchedInstrumentId = {commit.InstrumentId}
                    AND LastProcessedScheduledFor = {expected}
                """,
                cancellationToken);

            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return EvaluationCommitOutcome.CheckpointConflict;
            }

            if (commit.Intent is not null)
            {
                _context.NotificationIntents.Add(MapIntent(commit));

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception)
                {
                    if (IsIntentUniqueViolation(exception) is false)
                    {
                        throw;
                    }

                    await transaction.RollbackAsync(cancellationToken);
                    _context.ChangeTracker.Clear();
                    return await HealCheckpointAsync(commit, cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return EvaluationCommitOutcome.Committed;
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
                    "The evaluation commit was cancelled.",
                    exception,
                    cancellationToken);
            }

            throw new RecommendationStorageException(
                "Persisting the evaluation outcome failed.",
                exception);
        }
    }

    private async Task<EvaluationCommitOutcome> HealCheckpointAsync(
        RecommendationCommit commit,
        CancellationToken cancellationToken)
    {
        DateTime processedSlot = commit.ProcessedSlot.ToDateTimeUtc();
        DateTime expected = commit.ExpectedCheckpoint.ToDateTimeUtc();
        DateTime committedAt = commit.CommittedAt.ToDateTimeUtc();

        int updated = await _context.Database.ExecuteSqlAsync(
            $"""
            UPDATE RecommendationCheckpoints
            SET LastProcessedScheduledFor = {processedSlot},
                UpdatedAt = {committedAt}
            WHERE WatchedInstrumentId = {commit.InstrumentId}
                AND LastProcessedScheduledFor = {expected}
            """,
            cancellationToken);

        return updated > 0
            ? EvaluationCommitOutcome.AlreadyRecorded
            : EvaluationCommitOutcome.CheckpointConflict;
    }

    private static void Validate(RecommendationCommit commit)
    {
        PersistedInstant.Require(commit.ExpectedCheckpoint, nameof(commit));
        PersistedInstant.Require(commit.ProcessedSlot, nameof(commit));
        PersistedInstant.Require(commit.CommittedAt, nameof(commit));

        if (commit.Intent is not null)
        {
            TriggeredRecommendation intent = commit.Intent;
            if (Enum.IsDefined(intent.PriceBasis) is false)
            {
                throw new InvalidDataException(
                    $"The price basis '{intent.PriceBasis}' is not a defined value.");
            }

            PersistedInstant.Require(intent.ScheduledFor, nameof(commit));
            PersistedInstant.Require(intent.PreviousScheduledFor, nameof(commit));

            foreach (TriggeredCondition match in intent.Matches)
            {
                ArgumentNullException.ThrowIfNull(match, nameof(commit));
            }
        }
    }

    private static NotificationIntentRow MapIntent(RecommendationCommit commit)
    {
        TriggeredRecommendation intent = commit.Intent!;
        NotificationIntentRow row = new()
        {
            WatchedInstrumentId = intent.InstrumentId,
            ScheduledFor = intent.ScheduledFor,
            PreviousScheduledFor = intent.PreviousScheduledFor,
            MonitoringRuleRevisionId = intent.MonitoringRuleRevisionId,
            Reason = EvaluationReason.ConditionsTriggered.ToString(),
            PriceBasis = intent.PriceBasis.ToString(),
            QuoteCurrency = intent.QuoteCurrency,
            CurrentBid = intent.CurrentBid,
            CurrentAsk = intent.CurrentAsk,
            Status = "Pending",
            CreatedAt = commit.CommittedAt,
            UpdatedAt = commit.CommittedAt
        };

        foreach (TriggeredCondition match in intent.Matches)
        {
            row.Matches.Add(new NotificationIntentMatchRow
            {
                ZoneId = match.ZoneId.Value,
                Condition = match.Condition.ToString(),
                ActionId = match.ActionId.Value,
                Lower = match.Lower,
                Level = match.Level,
                Upper = match.Upper,
                PreviousPrice = match.PreviousPrice,
                CurrentPrice = match.CurrentPrice
            });
        }

        return row;
    }

    private static SavedPriceObservation MapObservation(PriceObservationRow row)
    {
        return new SavedPriceObservation(
            row.ScheduledFor,
            row.Bid,
            row.Ask,
            row.QuoteCurrency);
    }

    private static bool IsIntentUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException
        {
            Number: 2601 or 2627
        } sqlException
            && sqlException.Message.Contains(
                IntentIndexName,
                StringComparison.Ordinal);
    }
}
