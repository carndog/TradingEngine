namespace TradingEngine.Domain.Instruments;

public sealed record ExchangeCode
{
    private const int MaximumLength = 20;

    private ExchangeCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ExchangeCode From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException(
                ExchangeCodeRule.Required,
                "An exchange code is required.");
        }

        string normalizedValue = value.Trim().ToUpperInvariant();

        if (normalizedValue.Length > MaximumLength)
        {
            throw new DomainRuleViolationException(
                ExchangeCodeRule.ExceedsMaximumLength,
                $"An exchange code cannot exceed {MaximumLength} characters.");
        }

        if (normalizedValue.All(IsAllowedCharacter) is false)
        {
            throw new DomainRuleViolationException(
                ExchangeCodeRule.InvalidCharacters,
                "An exchange code may contain only ASCII letters, digits, periods, hyphens and underscores.");
        }

        return new ExchangeCode(normalizedValue);
    }

    public override string ToString()
    {
        return Value;
    }

    private static bool IsAllowedCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_';
    }
}
