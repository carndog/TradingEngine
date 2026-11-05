using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Ports;

public interface IMonitoredInstrumentSource
{
    Task<IReadOnlyList<MonitoredInstrument>> ListMonitoredAsync(
        CancellationToken cancellationToken);
}
