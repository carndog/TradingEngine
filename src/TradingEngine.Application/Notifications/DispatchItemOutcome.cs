namespace TradingEngine.Application.Notifications;

public enum DispatchItemOutcome
{
    Sent = 1,
    RetryPending = 2,
    Failed = 3,
    Ambiguous = 4,
    Suppressed = 5,
    ClaimLost = 6,
    ClaimSkipped = 7
}
