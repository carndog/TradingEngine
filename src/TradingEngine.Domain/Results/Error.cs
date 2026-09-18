namespace TradingEngine.Domain.Results;

public sealed record Error
{
    private Error(string code, string description, ErrorType type)
    {
        Code = code;
        Description = description;
        Type = type;
    }

    public string Code { get; }

    public string Description { get; }

    public ErrorType Type { get; }

    public static Error Validation(string code, string description)
    {
        return Create(code, description, ErrorType.Validation);
    }

    public static Error Conflict(string code, string description)
    {
        return Create(code, description, ErrorType.Conflict);
    }

    public static Error NotFound(string code, string description)
    {
        return Create(code, description, ErrorType.NotFound);
    }

    private static Error Create(string code, string description, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Error(code, description, type);
    }
}
