namespace TradingEngine.Application.MarketData;

public sealed record MarketListing(
    string Provider,
    long ProviderInstrumentId,
    string ProviderSymbol,
    string? ProviderExchangeName,
    int? ProviderExchangeId,
    string? DisplayName,
    string? NativeQuoteCurrency);
