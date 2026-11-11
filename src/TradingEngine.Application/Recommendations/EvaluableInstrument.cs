namespace TradingEngine.Application.Recommendations;

public sealed record EvaluableInstrument(
    Guid Id,
    string Symbol,
    string Exchange,
    string QuoteCurrency);
