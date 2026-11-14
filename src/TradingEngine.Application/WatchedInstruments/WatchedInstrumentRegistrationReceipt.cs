using TradingEngine.Application.Listings;

namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentRegistrationReceipt(
    Guid InstrumentId,
    ListingConfigurationSnapshot? Listing);
