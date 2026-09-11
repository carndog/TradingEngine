namespace TradingEngine.Domain.Instruments;

public sealed record CurrencyCode
{
    private const int RequiredLength = 3;

    private CurrencyCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static CurrencyCode From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A currency code is required.", nameof(value));
        }

        string normalizedValue = value.Trim().ToUpperInvariant();

        if (normalizedValue.Length != RequiredLength || !normalizedValue.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                "A currency code must contain exactly three ASCII letters.",
                nameof(value));
        }

        return new CurrencyCode(normalizedValue);
    }

    public override string ToString()
    {
        return Value;
    }
}
