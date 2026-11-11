namespace TradingEngine.Application.Notifications;

public sealed record EmailOperationProbe(
    EmailOperationStatus Status,
    string? ProviderStatus,
    string? ErrorKind);
