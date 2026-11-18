namespace TradingEngine.Application.Notifications;

public sealed record EmailDispatchResult(Guid DeliveryId, DispatchItemOutcome Outcome, string? ErrorKind);
