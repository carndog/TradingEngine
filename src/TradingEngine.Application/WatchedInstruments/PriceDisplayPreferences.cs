namespace TradingEngine.Application.WatchedInstruments;

public sealed record PriceDisplayPreferences(int DecimalPlaces, PriceDisplayUnit Unit)
{
    public const int MinimumDecimalPlaces = 0;
    public const int MaximumDecimalPlaces = 8;
    public const int DefaultDecimalPlaces = 2;

    public static readonly PriceDisplayPreferences Default = new(DefaultDecimalPlaces, PriceDisplayUnit.Major);
}
