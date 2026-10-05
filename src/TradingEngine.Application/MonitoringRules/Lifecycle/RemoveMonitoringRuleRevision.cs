namespace TradingEngine.Application.MonitoringRules.Lifecycle;

public sealed record RemoveMonitoringRuleRevision(
    Guid InstrumentId,
    Guid RevisionId,
    byte[]? ExpectedConcurrencyToken);
