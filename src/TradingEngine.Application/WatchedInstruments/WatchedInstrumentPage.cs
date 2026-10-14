namespace TradingEngine.Application.WatchedInstruments;

public sealed record WatchedInstrumentPage(
    IReadOnlyList<WatchedInstrumentSummary> Items,
    int Page,
    int PageSize,
    int TotalCount);
