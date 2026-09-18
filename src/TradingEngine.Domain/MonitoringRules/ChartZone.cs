using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.MonitoringRules;

public sealed record ChartZone
{
    private ChartZone(
        ChartAnalysisIdentifier id,
        decimal lower,
        decimal level,
        decimal upper,
        IReadOnlyList<ChartCondition> conditions)
    {
        Id = id;
        Lower = lower;
        Level = level;
        Upper = upper;
        Conditions = conditions;
    }

    public ChartAnalysisIdentifier Id { get; }

    public decimal Lower { get; }

    public decimal Level { get; }

    public decimal Upper { get; }

    public IReadOnlyList<ChartCondition> Conditions { get; }

    public static Result<ChartZone> Create(
        ChartAnalysisIdentifier id,
        decimal lower,
        decimal level,
        decimal upper,
        IReadOnlyList<ChartCondition> conditions)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(conditions);

        if (lower <= 0m || level <= 0m || upper <= 0m)
        {
            return ChartAnalysisErrors.ZoneNonPositivePrice;
        }

        if ((lower < level && level < upper) is false)
        {
            return ChartAnalysisErrors.ZoneInvalidBoundaryOrder;
        }

        if (conditions.Count == 0)
        {
            return ChartAnalysisErrors.ZoneMissingConditions;
        }

        foreach (ChartCondition condition in conditions)
        {
            ArgumentNullException.ThrowIfNull(condition);
        }

        return new ChartZone(id, lower, level, upper, conditions.ToArray());
    }
}
