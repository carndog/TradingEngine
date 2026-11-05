using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Tests.PriceCollection;

internal sealed class StubMonitoredInstrumentSource : IMonitoredInstrumentSource
{
    public List<MonitoredInstrument> Instruments { get; set; } = [];

    public int CallCount { get; private set; }

    public Func<CancellationToken, Task>? Gate { get; set; }

    public async Task<IReadOnlyList<MonitoredInstrument>> ListMonitoredAsync(
        CancellationToken cancellationToken)
    {
        if (Gate is not null)
        {
            await Gate(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Instruments;
    }
}
