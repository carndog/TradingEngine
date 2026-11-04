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

    public static readonly Error ProviderThrottled = Error.PreconditionFailed(
        "market_data.provider_throttled",
        "The provider rate limit prevented the request within the permitted time.");
}
