using NodaTime;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.MonitoringRules.Lifecycle;

public sealed record EditMonitoringRuleRevision(
    Guid InstrumentId,
    Guid RevisionId,
    ChartAnalysisDefinition? Definition,
    string? ChangeReason,
    Instant? EffectiveFrom,
    byte[]? ExpectedConcurrencyToken);
