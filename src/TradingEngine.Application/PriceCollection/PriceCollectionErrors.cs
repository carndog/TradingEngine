using TradingEngine.Domain.Results;

namespace TradingEngine.Application.PriceCollection;

public static class PriceCollectionErrors
{
    public static readonly Error ExchangeSessionUnconfigured = Error.Validation(
        "price_collection.exchange_session_unconfigured",
        "No exchange session calendar is configured for the instrument's exchange.");

    public static readonly Error ProviderTimeout = Error.PreconditionFailed(
        "price_collection.provider_timeout",
        "The market-data provider request timed out within its execution budget.");

    public static readonly Error ProviderError = Error.PreconditionFailed(
        "price_collection.provider_error",
        "The market-data provider request failed unexpectedly.");

    public static readonly Error DeadlineExceeded = Error.PreconditionFailed(
        "price_collection.deadline_exceeded",
        "The collection run reached its execution deadline before the attempt completed.");
}
