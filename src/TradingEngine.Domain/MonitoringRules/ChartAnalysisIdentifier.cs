namespace TradingEngine.Domain.MonitoringRules;

public sealed record ChartAnalysisIdentifier
{
    private const int MaximumLength = 64;

    private ChartAnalysisIdentifier(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ChartAnalysisIdentifier From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException(
                ChartAnalysisIdentifierRule.Required,
                "A chart-analysis identifier is required.");
        }

        if (value.Length > MaximumLength)
        {
            throw new DomainRuleViolationException(
                ChartAnalysisIdentifierRule.ExceedsMaximumLength,
                $"A chart-analysis identifier cannot exceed {MaximumLength} characters.");
        }

        if (IsAllowedStart(value[0]) is false || value.Skip(1).All(IsAllowedCharacter) is false)
        {
            throw new DomainRuleViolationException(
                ChartAnalysisIdentifierRule.InvalidCharacters,
                "A chart-analysis identifier must start with a lowercase ASCII letter and contain only lowercase ASCII letters, digits, periods and hyphens.");
        }

        return new ChartAnalysisIdentifier(value);
    }

    public override string ToString()
    {
        return Value;
    }

    private static bool IsAllowedStart(char character)
    {
        return character is >= 'a' and <= 'z';
    }

    private static bool IsAllowedCharacter(char character)
    {
        return IsAllowedStart(character)
            || character is >= '0' and <= '9'
            || character is '.' or '-';
    }
}
