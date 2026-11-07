using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WatchedInstruments;

public static class PriceDisplayErrors
{
    public static readonly Error DecimalPlacesOutOfRange = Error.Validation(
        "watched_instrument.price_display_decimal_places_out_of_range",
        "Price decimal places must be a whole number from 0 to 8.");

    public static readonly Error UnitUndefined = Error.Validation(
        "watched_instrument.price_display_unit_undefined",
        "Display unit must be major or native.");
}
