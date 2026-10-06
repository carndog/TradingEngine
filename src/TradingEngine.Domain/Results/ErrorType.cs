namespace TradingEngine.Domain.Results;

public enum ErrorType
{
    Validation = 1,
    Conflict = 2,
    NotFound = 3,
    PreconditionRequired = 4,
    PreconditionFailed = 5
}
