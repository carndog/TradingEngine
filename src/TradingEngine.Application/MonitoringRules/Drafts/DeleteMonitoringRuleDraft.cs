namespace TradingEngine.Application.MonitoringRules.Drafts;

public sealed record DeleteMonitoringRuleDraft(
    Guid InstrumentId,
    Guid DraftId,
    byte[]? ExpectedConcurrencyToken);
