namespace TradingEngine.Application.Recommendations;

public enum EvaluationCommitOutcome
{
    Committed = 1,
    AlreadyRecorded = 2,
    CheckpointConflict = 3
}
