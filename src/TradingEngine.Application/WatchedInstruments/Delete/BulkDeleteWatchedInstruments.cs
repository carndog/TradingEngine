namespace TradingEngine.Application.WatchedInstruments.Delete;

public sealed record BulkDeleteWatchedInstruments(
    IReadOnlyList<WatchedInstrumentDeleteItem> Items);
