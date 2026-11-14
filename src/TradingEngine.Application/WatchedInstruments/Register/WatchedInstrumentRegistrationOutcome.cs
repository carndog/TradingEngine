using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments.Register;

public sealed record WatchedInstrumentRegistrationOutcome(
    WatchedInstrument Instrument,
    ListingStatusInfo Listing);
