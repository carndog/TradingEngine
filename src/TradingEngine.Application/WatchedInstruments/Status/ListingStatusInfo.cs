using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments.Status;

public sealed record ListingStatusInfo(
    ListingUnitState UnitState,
    string? NativeQuoteCurrency,
    ListingUnitSource? UnitSource,
    bool DeploymentConfigured,
    InstrumentListing? Listing,
    byte[]? ConcurrencyToken,
    ListingResolutionState? ResolutionState,
    string? ResolutionDetailCode);
