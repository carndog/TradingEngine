using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Instruments;

public static class InstrumentListingErrors
{
    public static readonly Error ExchangeRequired = Error.Validation(
        "instrument_listing.exchange_required",
        "An exchange code is required.");

    public static readonly Error ExchangeExceedsMaximumLength = Error.Validation(
        "instrument_listing.exchange_exceeds_maximum_length",
        "An exchange code cannot exceed 20 characters.");

    public static readonly Error ExchangeInvalidCharacters = Error.Validation(
        "instrument_listing.exchange_invalid_characters",
        "An exchange code may contain only ASCII letters, digits, periods, hyphens and underscores.");

    public static readonly Error SymbolRequired = Error.Validation(
        "instrument_listing.symbol_required",
        "An instrument symbol is required.");

    public static readonly Error SymbolExceedsMaximumLength = Error.Validation(
        "instrument_listing.symbol_exceeds_maximum_length",
        "An instrument symbol cannot exceed 64 characters.");

    public static readonly Error SymbolContainsWhitespace = Error.Validation(
        "instrument_listing.symbol_contains_whitespace",
        "An instrument symbol cannot contain whitespace.");

    public static readonly Error ProviderRequired = Error.Validation(
        "instrument_listing.provider_required",
        "A market-data provider name is required.");

    public static readonly Error ProviderExceedsMaximumLength = Error.Validation(
        "instrument_listing.provider_exceeds_maximum_length",
        "A market-data provider name cannot exceed 32 characters.");

    public static readonly Error ProviderInstrumentIdInvalid = Error.Validation(
        "instrument_listing.provider_instrument_id_invalid",
        "A provider instrument identifier must be a positive value.");

    public static readonly Error ProviderFieldExceedsMaximumLength = Error.Validation(
        "instrument_listing.provider_field_exceeds_maximum_length",
        "A provider metadata value cannot exceed its supported length.");

    public static readonly Error NativeUnitRequired = Error.Validation(
        "instrument_listing.native_unit_required",
        "A native quote currency or unit code is required.");

    public static readonly Error NativeUnitInvalidFormat = Error.Validation(
        "instrument_listing.native_unit_invalid_format",
        "A native quote currency or unit code must contain between 3 and 10 ASCII letters or digits.");

    public static readonly Error UnitSourceUndefined = Error.Validation(
        "instrument_listing.unit_source_undefined",
        "The listing unit source is not a defined value.");

    public static readonly Error ConfirmedByRequired = Error.Validation(
        "instrument_listing.confirmed_by_required",
        "A confirming actor is required.");

    public static readonly Error ConfirmedByExceedsMaximumLength = Error.Validation(
        "instrument_listing.confirmed_by_exceeds_maximum_length",
        "A confirming actor cannot exceed 128 characters.");

    public static readonly Error LastChangedPrecedesConfirmed = Error.Validation(
        "instrument_listing.last_changed_precedes_confirmed",
        "The last-changed timestamp cannot precede the confirmation timestamp.");

    public static readonly Error NotFound = Error.NotFound(
        "instrument_listing.not_found",
        "No listing configuration exists for the requested exchange and symbol.");

    public static readonly Error UnitConflict = Error.Conflict(
        "instrument_listing.unit_conflict",
        "The requested native quote unit conflicts with the confirmed listing configuration and cannot be relabelled.");

    public static readonly Error ConcurrencyTokenRequired = Error.PreconditionRequired(
        "instrument_listing.concurrency_token_required",
        "A listing-configuration change requires the current concurrency token via the If-Match header.");

    public static readonly Error ConcurrencyTokenInvalid = Error.Validation(
        "instrument_listing.concurrency_token_invalid",
        "The supplied listing-configuration concurrency token is malformed.");

    public static readonly Error ConcurrentChange = Error.PreconditionFailed(
        "instrument_listing.concurrent_change",
        "The listing configuration changed after it was loaded; reload and retry the change.");
}
