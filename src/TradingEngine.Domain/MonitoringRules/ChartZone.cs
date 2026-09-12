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

    public static ChartZone Create(
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
            throw new DomainRuleViolationException(
                ChartZoneRule.NonPositivePrice,
                "Zone prices must be greater than zero.");
        }

        if ((lower < level && level < upper) is false)
        {
            throw new DomainRuleViolationException(
                ChartZoneRule.InvalidBoundaryOrder,
                "A zone must satisfy lower < level < upper.");
        }

        if (conditions.Count == 0)
        {
            throw new DomainRuleViolationException(
                ChartZoneRule.MissingConditions,
                "A zone requires at least one condition.");
        }

        foreach (ChartCondition condition in conditions)
        {
            ArgumentNullException.ThrowIfNull(condition);
        }

        return new ChartZone(id, lower, level, upper, conditions.ToArray());
    }
}
