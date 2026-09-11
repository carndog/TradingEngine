namespace TradingEngine.Domain.Instruments;

public sealed record QuoteCurrencyCode
{
    private const int RequiredLength = 3;

    private QuoteCurrencyCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static QuoteCurrencyCode From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A quote currency code is required.", nameof(value));
        }

        string normalizedValue = value.Trim().ToUpperInvariant();

        if (normalizedValue.Length != RequiredLength || !normalizedValue.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                "A quote currency code must contain exactly three ASCII letters.",
                nameof(value));
        }

        return new QuoteCurrencyCode(normalizedValue);
    }

    public override string ToString()
    {
        return Value;
    }
}
