namespace TradingEngine.Contracts.WatchedInstruments;

public sealed record ChartZoneDto(
    string? Id,
    decimal Lower,
    decimal Level,
    decimal Upper,
    IReadOnlyList<ChartConditionDto>? Conditions);
