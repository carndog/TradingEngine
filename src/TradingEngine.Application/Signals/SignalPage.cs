namespace TradingEngine.Application.Signals;

public sealed record SignalPage(
    IReadOnlyList<SignalStatus> Items,
    DateTimeOffset Cutoff,
    int Page,
    int PageSize,
    bool HasMore);
