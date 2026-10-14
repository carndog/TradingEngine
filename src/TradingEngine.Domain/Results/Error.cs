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

    public IReadOnlyDictionary<string, object?>? Extensions { get; private init; }

    public Error WithExtension(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Dictionary<string, object?> extensions = Extensions is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(Extensions);
        extensions[key] = value;

        return this with { Extensions = extensions };
    }

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

    public static Error PreconditionRequired(string code, string description)
    {
        return Create(code, description, ErrorType.PreconditionRequired);
    }

    public static Error PreconditionFailed(string code, string description)
    {
        return Create(code, description, ErrorType.PreconditionFailed);
    }

    private static Error Create(string code, string description, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Error(code, description, type);
    }
}
