namespace TradingEngine.Application.WatchedInstruments.Status;

public enum InstrumentCollectionStatus
{
    NotMonitored = 1,

    CollectorDisabled = 2,

    UnsupportedVenue = 3,

    NativeUnitUnconfirmed = 4,

    NativeUnitConflict = 5,

    SessionClosed = 6,

    AwaitingFirstObservation = 7,

    Collecting = 8
}
