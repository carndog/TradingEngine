namespace TradingEngine.Application.Notifications;

public enum NotificationAttemptOutcome
{
    Submitted = 1,
    Failed = 2,
    Ambiguous = 3,
    ResolvedSucceeded = 4,
    ResolvedRejected = 5,
    Unresolved = 6,
    Suppressed = 7
}
