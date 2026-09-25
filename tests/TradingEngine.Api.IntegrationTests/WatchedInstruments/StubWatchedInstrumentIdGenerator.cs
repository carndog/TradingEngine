using TradingEngine.Application.Ports;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

internal sealed class StubWatchedInstrumentIdGenerator : IWatchedInstrumentIdGenerator
{
    public Guid NextId { get; set; } = Guid.NewGuid();

    public Guid NewId()
    {
        return NextId;
    }
}
