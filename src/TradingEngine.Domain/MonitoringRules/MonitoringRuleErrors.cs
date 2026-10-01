using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.MonitoringRules;

public static class MonitoringRuleErrors
{
    public static readonly Error IdRequired = Error.Validation(
        "monitoring_rule.id_required",
        "A monitoring-rule identifier cannot be empty.");

    public static readonly Error WatchedInstrumentIdRequired = Error.Validation(
        "monitoring_rule.watched_instrument_id_required",
        "A monitoring rule must belong to a watched instrument.");
}
