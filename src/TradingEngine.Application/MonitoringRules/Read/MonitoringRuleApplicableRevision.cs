using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Revisions;

namespace TradingEngine.Application.MonitoringRules.Read;

public sealed record MonitoringRuleApplicableRevision(
    MonitoringRuleSnapshot Snapshot,
    Revision<ChartAnalysisDefinition> Revision);
