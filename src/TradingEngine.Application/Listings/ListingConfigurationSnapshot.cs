using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.Listings;

public sealed record ListingConfigurationSnapshot(
    InstrumentListing Listing,
    byte[] ConcurrencyToken);
