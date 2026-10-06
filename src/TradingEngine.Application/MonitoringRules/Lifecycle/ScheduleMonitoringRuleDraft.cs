using NodaTime;

namespace TradingEngine.Application.MonitoringRules.Lifecycle;

public sealed record ScheduleMonitoringRuleDraft(
    Guid InstrumentId,
    Guid DraftId,
    Instant EffectiveFrom,
    Instant? EffectiveTo,
    byte[]? ExpectedConcurrencyToken);
