using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.MonitoringRules;

public sealed record ChartCondition
{
    private ChartCondition(ChartConditionType type, ChartAnalysisIdentifier actionId)
    {
        Type = type;
        ActionId = actionId;
    }

    public ChartConditionType Type { get; }

    public ChartAnalysisIdentifier ActionId { get; }

    public static Result<ChartCondition> Create(ChartConditionType type, ChartAnalysisIdentifier actionId)
    {
        ArgumentNullException.ThrowIfNull(actionId);

        if (Enum.IsDefined(type) is false)
        {
            return ChartAnalysisErrors.ConditionTypeUndefined;
        }

        return new ChartCondition(type, actionId);
    }
}
