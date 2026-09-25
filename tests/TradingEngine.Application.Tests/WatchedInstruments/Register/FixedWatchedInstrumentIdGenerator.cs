using TradingEngine.Application.Ports;

namespace TradingEngine.Application.Tests.WatchedInstruments.Register;

internal sealed class FixedWatchedInstrumentIdGenerator : IWatchedInstrumentIdGenerator
{
    private readonly Guid _id;

    public FixedWatchedInstrumentIdGenerator(Guid id)
    {
        _id = id;
    }

    public Guid NewId()
    {
        return _id;
    }
}
