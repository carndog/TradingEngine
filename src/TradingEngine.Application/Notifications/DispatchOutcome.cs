using NodaTime;

namespace TradingEngine.Application.Notifications;

public sealed record DispatchOutcome(
    Guid IntentId,
    Guid ClaimToken,
    NotificationStatus Status,
    NotificationAttemptOutcome AttemptOutcome,
    Instant AttemptedAt,
    Instant RecordedAt,
    string? ProviderOperationId,
    string? ProviderStatus,
    string? ErrorKind);
