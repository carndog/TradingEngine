using TradingEngine.Application.Ports;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

internal sealed class StubWatchedInstrumentIdGenerator : IWatchedInstrumentIdGenerator
{
    public Guid NextId { get; set; } = Guid.Parse("9c7f2a31-84d5-4e6b-a1c2-3d4e5f607182");

    public Guid NewId()
    {
        return NextId;
    }
}
