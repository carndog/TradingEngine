namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record BulkDeleteWatchedInstrumentsRequest(
    IReadOnlyList<BulkDeleteWatchedInstrumentItemDto>? Items);
