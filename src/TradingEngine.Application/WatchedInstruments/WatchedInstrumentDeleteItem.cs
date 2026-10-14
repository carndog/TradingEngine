namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentDeleteItem(
    Guid InstrumentId,
    byte[]? ExpectedConcurrencyToken);
