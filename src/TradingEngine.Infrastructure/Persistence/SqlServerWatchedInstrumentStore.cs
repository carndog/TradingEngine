using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MonitoringRules.Xml;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerWatchedInstrumentStore : IWatchedInstrumentStore
{
    private const string BusinessKeyIndexName = "UX_WatchedInstruments_Exchange_Symbol_QuoteCurrency";

    private readonly TradingEngineDbContext _context;
    private readonly ChartAnalysisDefinitionXmlSerializer _serializer;

    public SqlServerWatchedInstrumentStore(
        TradingEngineDbContext context,
        ChartAnalysisDefinitionXmlSerializer serializer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async Task<Result> AddAsync(
        WatchedInstrumentConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        WatchedInstrument instrument = configuration.Instrument;
        WatchedInstrumentRow row = new()
        {
            Id = instrument.Id,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange,
            QuoteCurrency = instrument.QuoteCurrency,
            MonitoringState = instrument.MonitoringState.ToString(),
            SamplingIntervalSeconds = instrument.SamplingIntervalSeconds,
            CreatedAt = instrument.CreatedAt,
            LastChangedAt = instrument.LastChangedAt,
            ChartAnalysisDefinition = new ChartAnalysisDefinitionRow
            {
                WatchedInstrumentId = instrument.Id,
                DefinitionXml = _serializer.Serialize(configuration.Definition)
            }
        };

        _context.WatchedInstruments.Add(row);

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

        return Result.Success();
    }

    public async Task<Result<WatchedInstrumentConfiguration>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        WatchedInstrumentRow? row = await _context.WatchedInstruments
            .Include(instrument => instrument.ChartAnalysisDefinition)
            .SingleOrDefaultAsync(instrument => instrument.Id == instrumentId, cancellationToken);

        if (row is null)
        {
            return WatchedInstrumentErrors.ConfigurationNotFound;
        }

        if (row.ChartAnalysisDefinition is null)
        {
            throw new InvalidDataException(
                $"The persisted configuration for watched instrument '{instrumentId}' has no chart-analysis definition.");
        }

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
                $"The persisted watched instrument '{instrumentId}' is invalid " +
                $"({instrument.Error.Code}): {instrument.Error.Description}");
        }

        ChartAnalysisDefinition definition = _serializer.Deserialize(
            row.ChartAnalysisDefinition.DefinitionXml);

        return new WatchedInstrumentConfiguration(instrument.Value, definition);
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
