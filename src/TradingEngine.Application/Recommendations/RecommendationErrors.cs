using TradingEngine.Domain.Results;

namespace TradingEngine.Application.Recommendations;

public static class RecommendationErrors
{
    public static readonly Error DeadlineExceeded = Error.PreconditionFailed(
        "recommendations.deadline_exceeded",
        "The evaluation run reached its execution deadline before the instrument completed.");

    public static readonly Error RuleUnavailable = Error.PreconditionFailed(
        "recommendations.rule_unavailable",
        "The monitoring rule for the instrument could not be loaded for evaluation.");

    public static readonly Error BaselineObservationMissing = Error.PreconditionFailed(
        "recommendations.baseline_observation_missing",
        "The saved observation anchoring the evaluation checkpoint could not be read.");

    public static readonly Error CheckpointConflict = Error.Conflict(
        "recommendations.checkpoint_conflict",
        "The evaluation checkpoint moved while the slot was being processed.");

    public static readonly Error ObservationInvalid = Error.Validation(
        "recommendations.observation_invalid",
        "A saved observation could not be reconstructed for evaluation.");

    public static readonly Error StorageError = Error.PreconditionFailed(
        "recommendations.storage_error",
        "Persisting the evaluation outcome failed.");
}
