using NodaTime;

namespace TradingEngine.Application.Notifications;

public sealed record DispatchRunResult(
    Guid InvocationId,
    Instant StartedAt,
    Instant FinishedAt,
    IReadOnlyList<IntentDispatchResult> Items);
