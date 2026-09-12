namespace TradingEngine.Domain.Instruments;

public enum InstrumentSymbolRule
{
    Required,
    ExceedsMaximumLength,
    ContainsWhitespace
}
