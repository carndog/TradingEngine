using TradingEngine.Application.Ports;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

internal sealed class CapturingWatchedInstrumentStore : IWatchedInstrumentStore
{
    public WatchedInstrument? AddedInstrument { get; private set; }

    public Task AddAsync(WatchedInstrument instrument, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddedInstrument = instrument;
        return Task.CompletedTask;
    }
}
