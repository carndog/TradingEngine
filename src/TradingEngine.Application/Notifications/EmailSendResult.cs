namespace TradingEngine.Application.Notifications;

public sealed record EmailSendResult(
    EmailSendOutcome Outcome,
    string? ProviderOperationId,
    string? ErrorKind);
