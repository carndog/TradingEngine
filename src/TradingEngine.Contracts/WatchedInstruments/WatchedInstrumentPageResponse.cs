namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record WatchedInstrumentPageResponse(
    IReadOnlyList<WatchedInstrumentSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount);
