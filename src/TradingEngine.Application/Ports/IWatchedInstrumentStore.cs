using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.Ports;

public interface IWatchedInstrumentStore
{
    Task AddAsync(WatchedInstrument instrument, CancellationToken cancellationToken);
}
