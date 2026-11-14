namespace TradingEngine.Application.WatchedInstruments.Status;

public enum ListingResolutionState
{
    Resolved = 1,

    NotFound = 2,

    Ambiguous = 3,

    UnsupportedVenue = 4,

    Unavailable = 5
}
