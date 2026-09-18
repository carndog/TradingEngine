namespace TradingEngine.Domain.Results;

public sealed class Result
{
    private readonly Error? _error;

    private Result(Error? error)
    {
        _error = error;
    }

    public bool IsSuccess => _error is null;

    public bool IsFailure => _error is not null;

    public Error Error => _error
        ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success()
    {
        return new Result(null);
    }

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result(error);
    }

    public static implicit operator Result(Error error)
    {
        return Failure(error);
    }
}
