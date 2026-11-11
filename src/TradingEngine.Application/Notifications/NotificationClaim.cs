namespace TradingEngine.Application.Notifications;

public sealed record NotificationClaim(
    NotificationStatus Status,
    string? ProviderOperationId);
