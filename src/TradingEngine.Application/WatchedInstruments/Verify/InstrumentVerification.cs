using TradingEngine.Application.MarketData;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.WatchedInstruments.Status;

namespace TradingEngine.Application.WatchedInstruments.Verify;

public sealed record InstrumentVerification(
    MarketInstrumentIdentity Identity,
    ListingResolutionState ResolutionState,
    string? ResolutionDetailCode,
    MarketListing? Listing,
    string? NativeQuoteCurrency,
    string? ReadinessDetailCode,
    MarketSessionDecision Session,
    MarketQuote? Quote,
    string? QuoteDetailCode)
{
    public bool ReadyForMonitoring => Listing is not null && ReadinessDetailCode is null;
}
