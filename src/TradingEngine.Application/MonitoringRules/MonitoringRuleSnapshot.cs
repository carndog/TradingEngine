using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.MonitoringRules;

public sealed record MonitoringRuleSnapshot(MonitoringRule Rule, byte[] ConcurrencyToken);
