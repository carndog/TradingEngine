using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using TradingEngine.Application.MonitoringRules;
using TradingEngine.Application.Ports;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using TradingEngine.Infrastructure.MonitoringRules.Xml;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerMonitoringRuleStore : IMonitoringRuleStore
{
    private readonly TradingEngineDbContext _context;
    private readonly ChartAnalysisDefinitionXmlSerializer _serializer;

    public SqlServerMonitoringRuleStore(
        TradingEngineDbContext context,
        ChartAnalysisDefinitionXmlSerializer serializer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async Task<Result<MonitoringRuleSnapshot>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        MonitoringRuleRow? row = await _context.MonitoringRules
            .AsNoTracking()
            .Include(rule => rule.Revisions)
            .SingleOrDefaultAsync(
                rule => rule.WatchedInstrumentId == instrumentId,
                cancellationToken);

        if (row is null)
        {
            return MonitoringRuleErrors.NotFound;
        }

        MonitoringRule rule = MonitoringRuleRowMapping.Restore(row, _serializer);

        return new MonitoringRuleSnapshot(rule, row.RowVersion);
    }

    public async Task<Result> AddAsync(
        MonitoringRule rule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        MonitoringRuleRowMapping.ValidateInstants(rule);

        MonitoringRuleRow row = new()
        {
            Id = rule.Id,
            WatchedInstrumentId = rule.WatchedInstrumentId,
            CreatedAt = rule.CreatedAt
        };

        foreach (Revision<ChartAnalysisDefinition> revision in rule.Revisions.Concat(rule.Drafts))
        {
            row.Revisions.Add(MonitoringRuleRowMapping.ToRow(rule.Id, revision, _serializer));
        }

        _context.MonitoringRules.Add(row);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return MapUniqueViolation(exception);
        }

        return Result.Success();
    }

    public async Task<Result> SaveAsync(
        MonitoringRuleSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        MonitoringRuleRowMapping.ValidateInstants(snapshot.Rule);

        MonitoringRuleRow? root = await _context.MonitoringRules
            .Include(rule => rule.Revisions)
            .SingleOrDefaultAsync(
                rule => rule.Id == snapshot.Rule.Id,
                cancellationToken);

        if (root is null)
        {
            return MonitoringRuleErrors.NotFound;
        }

        if (root.WatchedInstrumentId != snapshot.Rule.WatchedInstrumentId)
        {
            throw new InvalidDataException(
                $"The persisted monitoring rule '{root.Id}' belongs to watched instrument " +
                $"'{root.WatchedInstrumentId}', not '{snapshot.Rule.WatchedInstrumentId}'.");
        }

        List<MonitoringRuleRevisionRow> desired = snapshot.Rule.Revisions
            .Concat(snapshot.Rule.Drafts)
            .Select(revision => MonitoringRuleRowMapping.ToRow(root.Id, revision, _serializer))
            .ToList();

        EntityEntry<MonitoringRuleRow> rootEntry = _context.Entry(root);
        rootEntry.Property(nameof(MonitoringRuleRow.RowVersion)).OriginalValue =
            snapshot.ConcurrencyToken;

        WithdrawRemovedAndChangedRevisions(root, desired);
        List<MonitoringRuleRevisionRow> adds = CollectAdds(root, desired);

        try
        {
            await using IDbContextTransaction transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken);

            rootEntry.State = EntityState.Modified;
            await _context.SaveChangesAsync(cancellationToken);

            foreach (MonitoringRuleRevisionRow add in adds)
            {
                _context.MonitoringRuleRevisions.Add(add);
            }

            ApplyFinalValues(root, desired);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MonitoringRuleErrors.ConcurrentChange;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return MapUniqueViolation(exception);
        }

        return Result.Success();
    }

    private void WithdrawRemovedAndChangedRevisions(
        MonitoringRuleRow root,
        List<MonitoringRuleRevisionRow> desired)
    {
        HashSet<Guid> desiredIds = desired.Select(row => row.Id).ToHashSet();
        List<MonitoringRuleRevisionRow> deleted = root.Revisions
            .Where(row => desiredIds.Contains(row.Id) is false)
            .ToList();

        foreach (MonitoringRuleRevisionRow row in deleted)
        {
            _context.MonitoringRuleRevisions.Remove(row);
            root.Revisions.Remove(row);
        }

        foreach (MonitoringRuleRevisionRow row in root.Revisions)
        {
            MonitoringRuleRevisionRow target = desired.First(candidate => candidate.Id == row.Id);
            bool committedStateChanged = row.RevisionNumber != target.RevisionNumber
                || row.EffectiveFrom != target.EffectiveFrom
                || row.EffectiveTo != target.EffectiveTo;

            if (committedStateChanged)
            {
                row.RevisionNumber = null;
                row.EffectiveFrom = null;
                row.EffectiveTo = null;
            }
        }
    }

    private static List<MonitoringRuleRevisionRow> CollectAdds(
        MonitoringRuleRow root,
        List<MonitoringRuleRevisionRow> desired)
    {
        HashSet<Guid> existingIds = root.Revisions.Select(row => row.Id).ToHashSet();

        return desired.Where(row => existingIds.Contains(row.Id) is false).ToList();
    }

    private static void ApplyFinalValues(
        MonitoringRuleRow root,
        List<MonitoringRuleRevisionRow> desired)
    {
        foreach (MonitoringRuleRevisionRow row in root.Revisions)
        {
            MonitoringRuleRevisionRow target = desired.First(candidate => candidate.Id == row.Id);
            row.RevisionNumber = target.RevisionNumber;
            row.CreatedBy = target.CreatedBy;
            row.ChangeReason = target.ChangeReason;
            row.DefinitionXml = target.DefinitionXml;
            row.EffectiveFrom = target.EffectiveFrom;
            row.EffectiveTo = target.EffectiveTo;
            row.ProposedFrom = target.ProposedFrom;
            row.ProposedTo = target.ProposedTo;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }

    private static Error MapUniqueViolation(DbUpdateException exception)
    {
        string message = exception.InnerException?.Message ?? exception.Message;

        if (message.Contains("PK_MonitoringRuleRevisions", StringComparison.Ordinal))
        {
            return RevisionErrors.DuplicateId;
        }

        if (message.Contains("UX_MonitoringRuleRevisions_EffectiveFrom", StringComparison.Ordinal))
        {
            return RevisionErrors.StartConflict;
        }

        if (message.Contains("UX_MonitoringRules_WatchedInstrumentId", StringComparison.Ordinal)
            || message.Contains("PK_MonitoringRules", StringComparison.Ordinal))
        {
            return MonitoringRuleErrors.AlreadyExists;
        }

        return MonitoringRuleErrors.ConcurrentChange;
    }
}
