namespace TradingEngine.Client.Services;

public sealed class ApiError
{
    private ApiError(
        int status,
        string? code,
        string? detail,
        TimeSpan? retryAfter,
        bool isNetworkError)
    {
        Status = status;
        Code = code;
        Detail = detail;
        RetryAfter = retryAfter;
        IsNetworkError = isNetworkError;
    }

    public int Status { get; }

    public string? Code { get; }

    public string? Detail { get; }

    public TimeSpan? RetryAfter { get; }

    public bool IsNetworkError { get; }

    public bool IsUnauthorized => Status == 401;

    public bool IsNotFound => Status == 404;

    public bool IsConflict => Status == 409;

    public bool IsPreconditionFailed => Status == 412;

    public bool IsPreconditionRequired => Status == 428;

    public bool IsRateLimited => Status == 429;

    public bool IsValidation => Status == 400;

    public static ApiError Create(
        int status,
        string? code,
        string? detail,
        TimeSpan? retryAfter)
    {
        return new ApiError(status, code, detail, retryAfter, false);
    }

    public static ApiError Network(string detail)
    {
        return new ApiError(0, "network.unreachable", detail, null, true);
    }
}
