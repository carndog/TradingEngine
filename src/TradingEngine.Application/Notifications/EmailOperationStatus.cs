namespace TradingEngine.Application.Notifications;

public enum EmailOperationStatus
{
    Unknown = 1,
    InFlight = 2,
    Succeeded = 3,
    Rejected = 4,
    ProbeFailed = 5
}
