using NodaTime;

namespace TradingEngine.Application.MonitoringRules.Read;

public sealed record GetApplicableMonitoringRuleRevision(Guid InstrumentId, Instant? At);
