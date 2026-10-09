using Microsoft.AspNetCore.Http.HttpResults;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api;

internal static class ApiProblemDetails
{
    internal static ProblemHttpResult Problem(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
            ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
            _ => StatusCodes.Status500InternalServerError
        };

        return TypedResults.Problem(
            statusCode: statusCode,
            detail: error.Description,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code
            });
    }

    internal static ProblemHttpResult Unauthorized(string code, string detail)
    {
        return TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
    }

    internal static ProblemHttpResult TooManyRequests()
    {
        return TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            detail: "Too many requests. Reduce the request rate and retry.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "rate_limit.exceeded"
            });
    }
}
