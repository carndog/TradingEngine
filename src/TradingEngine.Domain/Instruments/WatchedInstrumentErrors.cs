using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.Instruments;

public static class WatchedInstrumentErrors
{
    public static readonly Error IdRequired = Error.Validation(
        "watched_instrument.id_required",
        "A watched-instrument identifier cannot be empty.");

    public static readonly Error SymbolRequired = Error.Validation(
        "watched_instrument.symbol_required",
        "An instrument symbol is required.");

    public static readonly Error SymbolExceedsMaximumLength = Error.Validation(
        "watched_instrument.symbol_exceeds_maximum_length",
        "An instrument symbol cannot exceed 64 characters.");

    public static readonly Error SymbolContainsWhitespace = Error.Validation(
        "watched_instrument.symbol_contains_whitespace",
        "An instrument symbol cannot contain whitespace.");

    public static readonly Error ExchangeRequired = Error.Validation(
        "watched_instrument.exchange_required",
        "An exchange code is required.");

    public static readonly Error ExchangeExceedsMaximumLength = Error.Validation(
        "watched_instrument.exchange_exceeds_maximum_length",
        "An exchange code cannot exceed 20 characters.");

    public static readonly Error ExchangeInvalidCharacters = Error.Validation(
        "watched_instrument.exchange_invalid_characters",
        "An exchange code may contain only ASCII letters, digits, periods, hyphens and underscores.");

    public static readonly Error QuoteCurrencyRequired = Error.Validation(
        "watched_instrument.quote_currency_required",
        "A quote currency code is required.");

    public static readonly Error QuoteCurrencyInvalidFormat = Error.Validation(
        "watched_instrument.quote_currency_invalid_format",
        "A quote currency code must contain between 3 and 10 ASCII letters or digits.");

    public static readonly Error SamplingIntervalOutOfRange = Error.Validation(
        "watched_instrument.sampling_interval_out_of_range",
        "The sampling interval must be between 1 and 3600 seconds.");

    public static readonly Error AlreadyMonitored = Error.Conflict(
        "watched_instrument.already_monitored",
        "The instrument is already being monitored.");

    public static readonly Error NotMonitored = Error.Conflict(
        "watched_instrument.not_monitored",
        "The instrument is not currently being monitored.");

    public static readonly Error SamplingIntervalUnchanged = Error.Conflict(
        "watched_instrument.sampling_interval_unchanged",
        "The requested sampling interval is already assigned.");

    public static readonly Error ChangePrecedesLatestChange = Error.Conflict(
        "watched_instrument.change_precedes_latest_change",
        "A change cannot be recorded before the instrument's latest change.");

    public static readonly Error MonitoringStateUndefined = Error.Validation(
        "watched_instrument.monitoring_state_undefined",
        "The monitoring state is not a defined value.");

    public static readonly Error LastChangedPrecedesCreated = Error.Validation(
        "watched_instrument.last_changed_precedes_created",
        "The last-changed timestamp cannot precede the creation timestamp.");

    public static readonly Error DuplicateId = Error.Conflict(
        "watched_instrument.duplicate_id",
        "A watched instrument with the same identifier already exists.");

    public static readonly Error DuplicateBusinessKey = Error.Conflict(
        "watched_instrument.duplicate_business_key",
        "A watched instrument with the same exchange, symbol and quote currency already exists.");

    public static readonly Error RequestRequired = Error.Validation(
        "watched_instrument.request_required",
        "A watched-instrument request body is required.");

    public static readonly Error ConfigurationNotFound = Error.NotFound(
        "watched_instrument.configuration_not_found",
        "No configuration exists for the requested watched instrument.");
}
