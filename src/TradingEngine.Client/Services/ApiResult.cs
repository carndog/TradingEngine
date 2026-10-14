namespace TradingEngine.Client.Services;

public sealed class ApiResult<T>
{
    private ApiResult(T? value, ApiError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ApiError? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => Error is not null;

    public static ApiResult<T> Ok(T value)
    {
        return new ApiResult<T>(value, null);
    }

    public static ApiResult<T> Fail(ApiError error)
    {
        return new ApiResult<T>(default, error);
    }
}
