using NodaTime;
using TradingEngine.Application.Signals;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Ports;

public interface ISignalTrackingStore
{
    Task<Result<SignalTrackingState>> GetAsync(Guid signalId, CancellationToken cancellationToken);

    Task<Result<SignalTrackingState>> SetAsync(Guid signalId, bool archived, long expectedVersion,
        string actor, Instant at, CancellationToken cancellationToken);
}
