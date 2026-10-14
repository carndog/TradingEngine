using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.Revisions;
using TradingEngine.Infrastructure.MonitoringRules.Xml;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerWatchedInstrumentStore : IWatchedInstrumentStore
{
    private const string BusinessKeyIndexName = "UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency";
    private const string RegistrationCreatedBy = "watched-instrument-registration";

    private readonly TradingEngineDbContext _context;
    private readonly ChartAnalysisDefinitionXmlSerializer _serializer;

    public SqlServerWatchedInstrumentStore(
        TradingEngineDbContext context,
        ChartAnalysisDefinitionXmlSerializer serializer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async Task<Result<Guid>> AddAsync(
        WatchedInstrumentRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        WatchedInstrumentFields fields = registration.Fields;
        Instant createdAt = PersistedInstant.Require(
            registration.CreatedAt,
            nameof(registration));
        WatchedInstrumentRow row = new()
        {
            Symbol = fields.Symbol,
            Exchange = fields.Exchange,
            QuoteCurrency = fields.QuoteCurrency,
            MonitoringState = registration.MonitoringState.ToString(),
            SamplingIntervalSeconds = fields.SamplingIntervalSeconds,
            CreatedAt = createdAt,
            LastChangedAt = createdAt
        };

        _context.WatchedInstruments.Add(row);

        MonitoringRule rule = MonitoringRule.Create(
            Guid.CreateVersion7(),
            row.Id,
            Guid.CreateVersion7(),
            registration.Definition,
            createdAt,
            RegistrationCreatedBy).Value;
        MonitoringRuleRow ruleRow = new()
        {
            Id = rule.Id,
            WatchedInstrumentId = row.Id,
            CreatedAt = rule.CreatedAt
        };
        foreach (Revision<ChartAnalysisDefinition> revision in rule.Revisions.Concat(rule.Drafts))
        {
            ruleRow.Revisions.Add(
                MonitoringRuleRowMapping.ToRow(rule.Id, revision, _serializer));
        }

        _context.MonitoringRules.Add(ruleRow);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return IsBusinessKeyViolation(exception)
                ? WatchedInstrumentErrors.DuplicateBusinessKey
                : WatchedInstrumentErrors.DuplicateId;
        }

        return row.Id;
    }

    public async Task<Result<WatchedInstrumentConfiguration>> GetAsync(
        Guid instrumentId,
        Instant at,
        CancellationToken cancellationToken)
    {
        WatchedInstrumentRow? row = await _context.WatchedInstruments
            .SingleOrDefaultAsync(instrument => instrument.Id == instrumentId, cancellationToken);

        if (row is null)
        {
            return WatchedInstrumentErrors.ConfigurationNotFound;
        }

        Result<WatchedInstrument> instrument = Restore(row);

        MonitoringRuleRow? rule = await _context.MonitoringRules
            .AsNoTracking()
            .Include(candidate => candidate.Revisions)
            .SingleOrDefaultAsync(
                candidate => candidate.WatchedInstrumentId == instrumentId,
                cancellationToken);

        if (rule is null)
        {
            throw new InvalidDataException(
                $"The persisted configuration for watched instrument '{instrumentId}' has no monitoring rule.");
        }

        MonitoringRule restored = MonitoringRuleRowMapping.Restore(rule, _serializer);
        Revision<ChartAnalysisDefinition>? effective = restored.EffectiveAt(at);

        if (effective is null)
        {
            return MonitoringRuleErrors.NoApplicableRevision;
        }

        return new WatchedInstrumentConfiguration(instrument.Value, effective.Definition);
    }

    public async Task<Result<WatchedInstrumentPage>> SearchAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        IQueryable<WatchedInstrumentRow> candidates = _context.WatchedInstruments
            .AsNoTracking();

        if (string.IsNullOrWhiteSpace(query) is false)
        {
            string normalized = query.Trim().ToUpperInvariant();
            candidates = candidates.Where(instrument =>
                instrument.Symbol.ToUpper().Contains(normalized)
                || instrument.Exchange.ToUpper().Contains(normalized));
        }

        int totalCount = await candidates.CountAsync(cancellationToken);

        long requestedOffset = Math.Max(0L, (long)(page - 1) * pageSize);
        int offset = requestedOffset > int.MaxValue ? int.MaxValue : (int)requestedOffset;

        List<WatchedInstrumentRow> rows = await candidates
            .OrderBy(instrument => instrument.Symbol.ToUpper())
            .ThenBy(instrument => instrument.Exchange.ToUpper())
            .ThenBy(instrument => instrument.QuoteCurrency.ToUpper())
            .ThenBy(instrument => instrument.Id)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        WatchedInstrumentSummary[] items = rows
            .Select(row => new WatchedInstrumentSummary(
                row.Id,
                row.Symbol,
                row.Exchange,
                row.QuoteCurrency,
                ParseMonitoringState(row),
                row.SamplingIntervalSeconds))
            .ToArray();

        return new WatchedInstrumentPage(items, page, pageSize, totalCount);
    }

    public async Task<Result<WatchedInstrumentSettingsSnapshot>> GetSettingsAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        WatchedInstrumentRow? row = await _context.WatchedInstruments
            .AsNoTracking()
            .SingleOrDefaultAsync(instrument => instrument.Id == instrumentId, cancellationToken);

        if (row is null)
        {
            return WatchedInstrumentErrors.SettingsNotFound;
        }

        Result<WatchedInstrument> instrument = Restore(row);

        return new WatchedInstrumentSettingsSnapshot(instrument.Value, row.RowVersion);
    }

    public async Task<Result<WatchedInstrumentSettingsSnapshot>> SaveSettingsAsync(
        WatchedInstrument instrument,
        byte[] expectedConcurrencyToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(expectedConcurrencyToken);

        WatchedInstrumentRow? row = await _context.WatchedInstruments
            .SingleOrDefaultAsync(candidate => candidate.Id == instrument.Id, cancellationToken);

        if (row is null)
        {
            return WatchedInstrumentErrors.SettingsNotFound;
        }

        EntityEntry<WatchedInstrumentRow> entry = _context.Entry(row);
        entry.Property(nameof(WatchedInstrumentRow.RowVersion)).OriginalValue =
            expectedConcurrencyToken;

        row.MonitoringState = instrument.MonitoringState.ToString();
        row.SamplingIntervalSeconds = instrument.SamplingIntervalSeconds;
        row.LastChangedAt = PersistedInstant.Require(
            instrument.LastChangedAt,
            nameof(instrument));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return WatchedInstrumentErrors.ConcurrentChange;
        }

        return new WatchedInstrumentSettingsSnapshot(instrument, row.RowVersion);
    }

    private Result<WatchedInstrument> Restore(WatchedInstrumentRow row)
    {
        Result<WatchedInstrument> instrument = WatchedInstrument.Restore(
            row.Id,
            row.Symbol,
            row.Exchange,
            row.QuoteCurrency,
            ParseMonitoringState(row),
            row.SamplingIntervalSeconds,
            row.CreatedAt,
            row.LastChangedAt);

        if (instrument.IsFailure)
        {
            throw new InvalidDataException(
                $"The persisted watched instrument '{row.Id}' is invalid " +
                $"({instrument.Error.Code}): {instrument.Error.Description}");
        }

        return instrument;
    }

    private static MonitoringState ParseMonitoringState(WatchedInstrumentRow row)
    {
        if (Enum.TryParse(row.MonitoringState, out MonitoringState state))
        {
            return state;
        }

        throw new InvalidDataException(
            $"The persisted monitoring state '{row.MonitoringState}' for watched instrument " +
            $"'{row.Id}' is not a defined value.");
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }

    private static bool IsBusinessKeyViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && sqlException.Message.Contains(BusinessKeyIndexName, StringComparison.Ordinal);
    }
}
