using TradingEngine.Application.Ports;

namespace TradingEngine.Application.PriceCollection;

public sealed class PerInstrumentSamplingPolicy : ISamplingPolicy
{
    public string Name => SamplingPolicyNames.PerInstrument;

    public int EffectiveIntervalSeconds(
        MonitoredInstrument instrument,
        int monitoredCount)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        return instrument.SamplingIntervalSeconds;
    }
}
