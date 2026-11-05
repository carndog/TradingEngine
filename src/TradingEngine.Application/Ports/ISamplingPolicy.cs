using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Application.Ports;

public interface ISamplingPolicy
{
    string Name { get; }

    int EffectiveIntervalSeconds(MonitoredInstrument instrument, int monitoredCount);
}
