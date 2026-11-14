using TradingEngine.Application.Listings;

namespace TradingEngine.Application.MarketData;

public sealed record NativeQuoteUnitResolution(
    string? NativeQuoteCurrency,
    ListingConfigurationSnapshot? Persisted,
    string? DeploymentCurrency,
    bool SourceConflict);
