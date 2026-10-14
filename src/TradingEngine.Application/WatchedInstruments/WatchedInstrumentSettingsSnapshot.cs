using TradingEngine.Domain.Instruments;

namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentSettingsSnapshot(
    WatchedInstrument Instrument,
    byte[] ConcurrencyToken);
