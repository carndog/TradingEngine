using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Tests.PriceCollection;

internal sealed class StubMonitoredInstrumentSource : IMonitoredInstrumentSource
{
    public List<MonitoredInstrument> Instruments { get; set; } = [];

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<MonitoredInstrument>> ListMonitoredAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult<IReadOnlyList<MonitoredInstrument>>(Instruments);
    }
}
