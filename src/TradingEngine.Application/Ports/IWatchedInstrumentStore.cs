using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface IWatchedInstrumentStore
{
    Task<Result> AddAsync(
        WatchedInstrumentConfiguration configuration,
        CancellationToken cancellationToken);

    Task<Result<WatchedInstrumentConfiguration>> GetAsync(
        Guid instrumentId,
        CancellationToken cancellationToken);
}
