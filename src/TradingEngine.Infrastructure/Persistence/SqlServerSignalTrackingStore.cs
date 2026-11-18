using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Signals;
using TradingEngine.Application.Time;
using TradingEngine.Domain.Results;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerSignalTrackingStore(TradingEngineDbContext context) : ISignalTrackingStore
{
    public async Task<Result<SignalTrackingState>> GetAsync(Guid signalId, CancellationToken cancellationToken)
    {
        if (await context.NotificationIntentMatches.AnyAsync(row => row.Id == signalId, cancellationToken) is false)
        {
            return ReadSignalsHandler.NotFound;
        }

        SignalTrackingAuditRow? latest = await LatestAsync(signalId, cancellationToken);
        return State(signalId, latest);
    }

    public async Task<Result<SignalTrackingState>> SetAsync(Guid signalId, bool archived, long expectedVersion,
        string actor, Instant at, CancellationToken cancellationToken)
    {
        PersistedInstant.Require(at, nameof(at));
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        NotificationIntentMatchRow? signal = await context.NotificationIntentMatches
            .FromSqlInterpolated($"SELECT * FROM NotificationIntentMatches WITH (UPDLOCK, HOLDLOCK) WHERE Id = {signalId}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (signal is null)
        {
            return ReadSignalsHandler.NotFound;
        }

        SignalTrackingAuditRow? latest = await LatestAsync(signalId, cancellationToken);
        SignalTrackingState state = State(signalId, latest);
        if (state.Version != expectedVersion)
        {
            return ChangeSignalTrackingHandler.StaleToken;
        }

        if (state.Archived == archived)
        {
            await transaction.CommitAsync(cancellationToken);
            return state;
        }

        if (latest is not null && at < latest.ChangedAt)
        {
            return Error.Conflict("signals.clock_regressed", "The audit clock is earlier than the last tracking change.");
        }

        SignalTrackingAuditRow audit = new() { SignalId = signalId, Archived = archived, ChangedAt = at, ChangedBy = actor };
        context.SignalTrackingAudit.Add(audit);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return State(signalId, audit);
    }

    private Task<SignalTrackingAuditRow?> LatestAsync(Guid signalId, CancellationToken cancellationToken)
    {
        return context.SignalTrackingAudit.AsNoTracking().Where(row => row.SignalId == signalId)
            .OrderByDescending(row => row.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static SignalTrackingState State(Guid signalId, SignalTrackingAuditRow? row)
    {
        return new SignalTrackingState(signalId, row?.Archived ?? false, row?.Id ?? 0, row?.ChangedAt.ToDateTimeOffset());
    }
}
