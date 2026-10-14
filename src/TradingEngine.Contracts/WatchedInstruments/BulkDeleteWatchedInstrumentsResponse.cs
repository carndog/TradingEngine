namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record BulkDeleteWatchedInstrumentsResponse(
    IReadOnlyList<Guid> DeletedIds,
    IReadOnlyList<Guid> AlreadyDeletedIds);
