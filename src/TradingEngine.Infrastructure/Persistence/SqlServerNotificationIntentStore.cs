using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerNotificationIntentStore : INotificationIntentStore
{
    private readonly TradingEngineDbContext _context;

    public SqlServerNotificationIntentStore(TradingEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<PendingNotification>> ListDispatchableAsync(
        int maxCount,
        Instant now,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);

        List<DispatchableIntent> rows = await _context.NotificationIntents
            .AsNoTracking()
            .Where(intent =>
                (intent.Status == "Pending"
                    || intent.Status == "Ambiguous"
                    || intent.Status == "Submitted")
                && (intent.ClaimedUntil == null || intent.ClaimedUntil <= now))
            .Join(
                _context.WatchedInstruments.Where(
                    instrument => instrument.DeletedAt == null),
                intent => intent.WatchedInstrumentId,
                instrument => instrument.Id,
                (intent, instrument) => new DispatchableIntent
                {
                    Intent = intent,
                    Symbol = instrument.Symbol,
                    Exchange = instrument.Exchange
                })
            .OrderBy(row =>
                _context.NotificationAttempts.Count(attempt =>
                    attempt.NotificationIntentId == row.Intent.Id
                    && (attempt.Outcome == "Submitted"
                        || attempt.Outcome == "Failed"
                        || attempt.Outcome == "Ambiguous")))
            .ThenBy(row => row.Intent.ScheduledFor)
            .ThenBy(row => row.Intent.Id)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        Guid[] intentIds = rows
            .Select(row => row.Intent.Id)
            .ToArray();

        List<NotificationIntentMatchRow> matches = await _context.NotificationIntentMatches
            .AsNoTracking()
            .Where(match => intentIds.Contains(match.NotificationIntentId))
            .OrderBy(match => match.Id)
            .ToListAsync(cancellationToken);

        List<AttemptCountRow> attemptCounts = await _context.NotificationAttempts
            .AsNoTracking()
            .Where(attempt =>
                intentIds.Contains(attempt.NotificationIntentId)
                && (attempt.Outcome == "Submitted"
                    || attempt.Outcome == "Failed"
                    || attempt.Outcome == "Ambiguous"))
            .GroupBy(attempt => attempt.NotificationIntentId)
            .Select(group => new AttemptCountRow
            {
                NotificationIntentId = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        ILookup<Guid, NotificationIntentMatchRow> matchesByIntent =
            matches.ToLookup(match => match.NotificationIntentId);
        Dictionary<Guid, int> countsByIntent = attemptCounts
            .ToDictionary(row => row.NotificationIntentId, row => row.Count);

        List<PendingNotification> pending = [];
        foreach (DispatchableIntent row in rows)
        {
            countsByIntent.TryGetValue(row.Intent.Id, out int attemptCount);
            pending.Add(Map(row, matchesByIntent[row.Intent.Id], attemptCount));
        }

        return pending;
    }

    public async Task<NotificationClaim?> TryClaimAsync(
        Guid intentId,
        Guid claimToken,
        Instant claimedUntil,
        Instant now,
        CancellationToken cancellationToken)
    {
        DateTime untilUtc = PersistedInstant
            .Require(claimedUntil, nameof(claimedUntil))
            .ToDateTimeUtc();
        DateTime nowUtc = PersistedInstant
            .Require(now, nameof(now))
            .ToDateTimeUtc();

        DbConnection connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            "UPDATE NotificationIntents " +
            "SET ClaimToken = @claimToken, " +
            "    ClaimedUntil = @claimedUntil, " +
            "    UpdatedAt = @now " +
            "OUTPUT INSERTED.Status, INSERTED.ProviderOperationId " +
            "WHERE Id = @intentId " +
            "    AND Status IN ('Pending', 'Ambiguous', 'Submitted') " +
            "    AND (ClaimedUntil IS NULL OR ClaimedUntil <= @now)";
        command.Parameters.Add(new SqlParameter("@claimToken", claimToken));
        command.Parameters.Add(new SqlParameter("@claimedUntil", untilUtc));
        command.Parameters.Add(new SqlParameter("@now", nowUtc));
        command.Parameters.Add(new SqlParameter("@intentId", intentId));

        await using DbDataReader reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (await reader.ReadAsync(cancellationToken) is false)
        {
            return null;
        }

        string status = reader.GetString(0);
        string? providerOperationId = await reader.IsDBNullAsync(1, cancellationToken)
            ? null
            : reader.GetString(1);

        return new NotificationClaim(
            Enum.Parse<NotificationStatus>(status),
            providerOperationId);
    }

    public async Task<BeginSubmissionResult> TryBeginSubmissionAsync(
        Guid intentId,
        Guid claimToken,
        string providerOperationId,
        int maxAttempts,
        Instant now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerOperationId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        DateTime nowUtc = PersistedInstant
            .Require(now, nameof(now))
            .ToDateTimeUtc();

        DbConnection connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            "IF (SELECT COUNT(*) FROM NotificationAttempts " +
            "    WHERE NotificationIntentId = @intentId " +
            "        AND Outcome IN ('Submitted', 'Failed', 'Ambiguous')) " +
            "    >= @maxAttempts " +
            "    SELECT -1 " +
            "ELSE " +
            "BEGIN " +
            "    UPDATE NotificationIntents " +
            "    SET ProviderOperationId = @providerOperationId, " +
            "        UpdatedAt = @now " +
            "    WHERE Id = @intentId " +
            "        AND ClaimToken = @claimToken " +
            "        AND Status IN ('Pending', 'Ambiguous', 'Submitted') " +
            "    SELECT @@ROWCOUNT " +
            "END";
        command.Parameters.Add(new SqlParameter("@intentId", intentId));
        command.Parameters.Add(new SqlParameter("@claimToken", claimToken));
        command.Parameters.Add(
            new SqlParameter("@providerOperationId", providerOperationId));
        command.Parameters.Add(new SqlParameter("@maxAttempts", maxAttempts));
        command.Parameters.Add(new SqlParameter("@now", nowUtc));

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        int rows = result is null ? 0 : (int)result;

        if (rows < 0)
        {
            return BeginSubmissionResult.AttemptsExhausted;
        }

        return rows > 0
            ? BeginSubmissionResult.Began
            : BeginSubmissionResult.ClaimLost;
    }

    public async Task<bool> RecordOutcomeAsync(
        DispatchOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Validate(outcome);

        string status = outcome.Status.ToString();
        string attemptOutcome = outcome.AttemptOutcome.ToString();
        DateTime attemptedAt = outcome.AttemptedAt.ToDateTimeUtc();
        DateTime recordedAt = outcome.RecordedAt.ToDateTimeUtc();

        try
        {
            await using IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync(cancellationToken);

            int updated = await _context.Database.ExecuteSqlAsync(
                $"""
                UPDATE NotificationIntents
                SET Status = {status},
                    ProviderOperationId = COALESCE(
                        {outcome.ProviderOperationId},
                        ProviderOperationId),
                    ClaimToken = NULL,
                    ClaimedUntil = NULL,
                    UpdatedAt = {recordedAt}
                WHERE Id = {outcome.IntentId}
                    AND ClaimToken = {outcome.ClaimToken}
                """,
                cancellationToken);

            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            _context.NotificationAttempts.Add(new NotificationAttemptRow
            {
                NotificationIntentId = outcome.IntentId,
                AttemptedAt = outcome.AttemptedAt,
                Outcome = attemptOutcome,
                ProviderOperationId = outcome.ProviderOperationId,
                ProviderStatus = outcome.ProviderStatus,
                ErrorKind = outcome.ErrorKind,
                RecordedAt = outcome.RecordedAt
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
                    "The dispatch outcome save was cancelled.",
                    exception,
                    cancellationToken);
            }

            throw new RecommendationStorageException(
                "Persisting the notification dispatch outcome failed.",
                exception);
        }
    }

    private static void Validate(DispatchOutcome outcome)
    {
        if (Enum.IsDefined(outcome.Status) is false
            || Enum.IsDefined(outcome.AttemptOutcome) is false)
        {
            throw new InvalidDataException(
                "The dispatch outcome carries an undefined status value.");
        }

        PersistedInstant.Require(outcome.AttemptedAt, nameof(outcome));
        PersistedInstant.Require(outcome.RecordedAt, nameof(outcome));
    }

    private static PendingNotification Map(
        DispatchableIntent row,
        IEnumerable<NotificationIntentMatchRow> matches,
        int attemptCount)
    {
        NotificationIntentRow intent = row.Intent;

        List<TriggeredCondition> conditions = matches
            .Select(match => new TriggeredCondition(
                ChartAnalysisIdentifier.From(match.ZoneId).Value,
                Enum.Parse<ChartConditionType>(match.Condition),
                ChartAnalysisIdentifier.From(match.ActionId).Value,
                match.Lower,
                match.Level,
                match.Upper,
                match.PreviousPrice,
                match.CurrentPrice))
            .ToList();

        return new PendingNotification(
            intent.Id,
            intent.WatchedInstrumentId,
            row.Symbol,
            row.Exchange,
            intent.ScheduledFor,
            intent.PreviousScheduledFor,
            intent.MonitoringRuleRevisionId,
            Enum.Parse<PriceBasis>(intent.PriceBasis),
            intent.QuoteCurrency,
            intent.CurrentBid,
            intent.CurrentAsk,
            conditions,
            Enum.Parse<NotificationStatus>(intent.Status),
            attemptCount,
            intent.CollectionInvocationId);
    }

    private sealed class DispatchableIntent
    {
        public NotificationIntentRow Intent { get; set; } = null!;

        public string Symbol { get; set; } = string.Empty;

        public string Exchange { get; set; } = string.Empty;
    }

    private sealed class AttemptCountRow
    {
        public Guid NotificationIntentId { get; set; }

        public int Count { get; set; }
    }
}
