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

    public static readonly Error PriceUnavailable = Error.PreconditionFailed(
        "price_collection.price_unavailable",
        "The provider returned a quote with neither bid nor ask after the bounded retry.");

    public static readonly Error UnsupportedQuote = Error.PreconditionFailed(
        "price_collection.unsupported_quote",
        "The provider quote cannot be persisted without loss or is outside the supported storage range.");

    public static readonly Error StorageError = Error.PreconditionFailed(
        "price_collection.storage_error",
        "Persisting the collection result failed.");
}
