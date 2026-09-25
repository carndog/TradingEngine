namespace TradingEngine.Application.Ports;

public interface IWatchedInstrumentIdGenerator
{
    Guid NewId();
}
