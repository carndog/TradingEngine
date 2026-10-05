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

    public static readonly Error ConcurrentChange = Error.PreconditionFailed(
        "monitoring_rule.concurrent_change",
        "The monitoring rule changed after it was loaded; reload and retry the change.");

    public static readonly Error NoApplicableRevision = Error.NotFound(
        "monitoring_rule.no_applicable_revision",
        "No committed monitoring-rule revision applies at the requested instant.");

    public static readonly Error InstantInvalid = Error.Validation(
        "monitoring_rule.instant_invalid",
        "A timestamp is required and must be a valid ISO-8601 UTC instant.");

    public static readonly Error InstantNotPersistable = Error.Validation(
        "monitoring_rule.instant_not_persistable",
        "A stored timestamp must lie within the supported UTC range and be no finer than 100 nanoseconds.");

    public static readonly Error RequestRequired = Error.Validation(
        "monitoring_rule.request_required",
        "A monitoring-rule request body is required.");

    public static readonly Error DefinitionRequired = Error.Validation(
        "monitoring_rule.definition_required",
        "A chart-analysis definition is required.");

    public static readonly Error ConcurrencyTokenRequired = Error.PreconditionRequired(
        "monitoring_rule.concurrency_token_required",
        "A monitoring-rule change requires the current concurrency token via the If-Match header.");

    public static readonly Error ConcurrencyTokenInvalid = Error.Validation(
        "monitoring_rule.concurrency_token_invalid",
        "The supplied concurrency token is malformed.");
}
