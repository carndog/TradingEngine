namespace TradingEngine.Domain.Instruments;

public sealed record InstrumentSymbol
{
    private const int MaximumLength = 64;

    private InstrumentSymbol(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static InstrumentSymbol From(string value)
    {
        string normalizedValue = Normalize(value, nameof(value));

        if (normalizedValue.Length > MaximumLength)
        {
            throw new DomainRuleViolationException(
                InstrumentSymbolRule.ExceedsMaximumLength,
                $"An instrument symbol cannot exceed {MaximumLength} characters.");
        }

        if (normalizedValue.Any(char.IsWhiteSpace))
        {
            throw new DomainRuleViolationException(
                InstrumentSymbolRule.ContainsWhitespace,
                "An instrument symbol cannot contain whitespace.");
        }

        return new InstrumentSymbol(normalizedValue);
    }

    public override string ToString()
    {
        return Value;
    }

    private static string Normalize(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException(
                InstrumentSymbolRule.Required,
                "An instrument symbol is required.");
        }

        return value.Trim().ToUpperInvariant();
    }
}
