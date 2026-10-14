namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentBulkDeleteOutcome(
    IReadOnlyList<Guid> DeletedIds,
    IReadOnlyList<Guid> AlreadyDeletedIds);
