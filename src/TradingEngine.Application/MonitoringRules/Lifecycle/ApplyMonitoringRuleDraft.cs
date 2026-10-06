using NodaTime;

namespace TradingEngine.Application.MonitoringRules.Lifecycle;

public sealed record ApplyMonitoringRuleDraft(
    Guid InstrumentId,
    Guid DraftId,
    Instant? EffectiveTo,
    byte[]? ExpectedConcurrencyToken);
