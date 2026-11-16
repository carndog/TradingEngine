namespace TradingEngine.Application.PriceCollection;

public sealed record ExchangeTradingDay(
    TradingDayKind Kind,
    string Reason,
    string? Version = null,
    string? TimeZone = null,
    IReadOnlyList<TradingDayWindow>? Windows = null,
    string? Notice = null);
