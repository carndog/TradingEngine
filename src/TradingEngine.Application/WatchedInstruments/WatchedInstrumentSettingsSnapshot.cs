using TradingEngine.Application.WatchedInstruments.Status;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentSettingsSnapshot(
    WatchedInstrument Instrument,
    byte[] ConcurrencyToken)
{
    public PriceDisplayPreferences Display { get; init; } = PriceDisplayPreferences.Default;

    public ListingStatusInfo? Listing { get; init; }

    public CollectionStatusInfo? Collection { get; init; }
}
