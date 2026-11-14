using TradingEngine.Domain.Results;

namespace TradingEngine.Application.MarketData;

public static class MarketDataErrors
{
    public static readonly Error ExchangeMappingUnconfigured = Error.NotFound(
        "market_data.exchange_mapping_unconfigured",
        "No provider mapping is configured for the instrument's exchange.");

    public static readonly Error InstrumentMappingUnresolved = Error.NotFound(
        "market_data.instrument_mapping_unresolved",
        "The provider has no instrument matching the requested exchange, symbol and quote currency.");

    public static readonly Error InstrumentMappingAmbiguous = Error.Conflict(
        "market_data.instrument_mapping_ambiguous",
        "The provider returned more than one instrument matching the requested mapping.");

    public static readonly Error PriceUnavailable = Error.NotFound(
        "market_data.price_unavailable",
        "The provider returned no current price for the instrument.");

    public static readonly Error QuoteCurrencyUnconfigured = Error.NotFound(
        "market_data.quote_currency_unconfigured",
        "No provider quote-currency mapping is configured for the instrument's exchange and symbol.");

    public static readonly Error QuoteCurrencyConflict = Error.Conflict(
        "market_data.quote_currency_conflict",
        "The configured provider quote currency conflicts with the requested quote currency.");

    public static readonly Error QuoteCurrencySourceConflict = Error.Conflict(
        "market_data.quote_currency_source_conflict",
        "The persisted listing configuration conflicts with the deployment-configured quote currency mapping.");

    public static readonly Error SearchPaginationIncomplete = Error.PreconditionFailed(
        "market_data.search_pagination_incomplete",
        "The provider did not return all search result pages within the permitted bounds.");

    public static readonly Error ProviderThrottled = Error.PreconditionFailed(
        "market_data.provider_throttled",
        "The provider rate limit prevented the request within the permitted time.");
}
