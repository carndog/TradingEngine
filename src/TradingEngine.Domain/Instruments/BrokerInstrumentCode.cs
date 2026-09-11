namespace TradingEngine.Domain.Instruments;

public sealed record BrokerInstrumentCode
{
    private const int MaximumLength = 64;

    private BrokerInstrumentCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static BrokerInstrumentCode From(string value)
    {
        string normalizedValue = Normalize(value, nameof(value));

        if (normalizedValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A broker instrument code cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        if (normalizedValue.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A broker instrument code cannot contain whitespace.", nameof(value));
        }

        return new BrokerInstrumentCode(normalizedValue);
    }

    public override string ToString()
    {
        return Value;
    }

    private static string Normalize(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A broker instrument code is required.", parameterName);
        }

        return value.Trim().ToUpperInvariant();
    }
}
