using TradingEngine.Domain.Results;

namespace TradingEngine.Application.PriceObservations;

public static class PriceObservationErrors
{
    public static readonly Error InstrumentNotFound = Error.NotFound(
        "price_observation.instrument_not_found",
        "No watched instrument exists for the requested identifier.");

    public static readonly Error PageSizeOutOfRange = Error.Validation(
        "price_observation.page_size_out_of_range",
        "The requested page size must be between 1 and 100.");

    public static readonly Error CursorInvalid = Error.Validation(
        "price_observation.cursor_invalid",
        "The before cursor must be a UTC ISO-8601 timestamp ending in Z, with at most seven fractional-second digits, within the stored range.");
}
