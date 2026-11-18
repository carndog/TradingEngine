namespace TradingEngine.Application.Signals;

public enum SignalFreshness
{
    Missing = 1,
    Current = 2,
    LatestSession = 3,
    StaleLastKnown = 4,
    UnknownCadence = 5,
    UnknownCalendar = 6,
    UnknownExpectedCollection = 7
}
