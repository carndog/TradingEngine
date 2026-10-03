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

    public static readonly Error NotFound = Error.NotFound(
        "monitoring_rule.not_found",
        "No monitoring rule exists for the requested watched instrument.");

    public static readonly Error AlreadyExists = Error.Conflict(
        "monitoring_rule.already_exists",
        "A monitoring rule already exists for the watched instrument.");

    public static readonly Error ConcurrentChange = Error.Conflict(
        "monitoring_rule.concurrent_change",
        "The monitoring rule changed after it was loaded; reload and retry the change.");

    public static readonly Error NoApplicableRevision = Error.NotFound(
        "monitoring_rule.no_applicable_revision",
        "No committed monitoring-rule revision applies at the requested instant.");
}
