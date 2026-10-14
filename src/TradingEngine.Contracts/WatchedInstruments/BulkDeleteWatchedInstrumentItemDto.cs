namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record BulkDeleteWatchedInstrumentItemDto(
    Guid InstrumentId,
    string? ConcurrencyToken);
