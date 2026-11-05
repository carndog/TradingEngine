using Microsoft.EntityFrameworkCore;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerMonitoredInstrumentSource : IMonitoredInstrumentSource
{
    private readonly TradingEngineDbContext _context;

    public SqlServerMonitoredInstrumentSource(TradingEngineDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<MonitoredInstrument>> ListMonitoredAsync(
        CancellationToken cancellationToken)
    {
        string monitored = MonitoringState.Monitored.ToString();

        List<MonitoredInstrument> instruments = await _context.WatchedInstruments
            .AsNoTracking()
            .Where(row => row.MonitoringState == monitored && row.DeletedAt == null)
            .OrderBy(row => row.Symbol)
            .ThenBy(row => row.Exchange)
            .ThenBy(row => row.QuoteCurrency)
            .ThenBy(row => row.Id)
            .Select(row => new MonitoredInstrument(
                row.Id,
                row.Exchange,
                row.Symbol,
                row.QuoteCurrency,
                row.SamplingIntervalSeconds))
            .ToListAsync(cancellationToken);

        return instruments;
    }
}
