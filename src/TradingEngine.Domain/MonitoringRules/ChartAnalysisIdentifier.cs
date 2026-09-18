using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.MonitoringRules;

public sealed record ChartAnalysisIdentifier
{
    private const int MaximumLength = 64;

    private ChartAnalysisIdentifier(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<ChartAnalysisIdentifier> From(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ChartAnalysisErrors.IdentifierRequired;
        }

        if (value.Length > MaximumLength)
        {
            return ChartAnalysisErrors.IdentifierExceedsMaximumLength;
        }

        if (IsAllowedStart(value[0]) is false || value.Skip(1).All(IsAllowedCharacter) is false)
        {
            return ChartAnalysisErrors.IdentifierInvalidCharacters;
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
