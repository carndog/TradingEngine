namespace TradingEngine.Application.WatchedInstruments.Search;

public sealed record SearchWatchedInstruments(
    string? Query,
    int Page,
    int PageSize);
