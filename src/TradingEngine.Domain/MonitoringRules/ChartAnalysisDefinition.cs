namespace TradingEngine.Domain.MonitoringRules;

public sealed record ChartAnalysisDefinition
{
    private const int MaximumPriceScale = 8;

    private static readonly ChartConditionType[] SupportConditionOrder =
    [
        ChartConditionType.BuyZone,
        ChartConditionType.SupportLoss
    ];

    private static readonly ChartConditionType[] ResistanceConditionOrder =
    [
        ChartConditionType.Breakout
    ];

    private ChartAnalysisDefinition(
        int priceScale,
        IReadOnlyList<ChartZone> supportZones,
        IReadOnlyList<ChartZone> resistanceZones)
    {
        PriceScale = priceScale;
        SupportZones = supportZones;
        ResistanceZones = resistanceZones;
    }

    public int PriceScale { get; }

    public IReadOnlyList<ChartZone> SupportZones { get; }

    public IReadOnlyList<ChartZone> ResistanceZones { get; }

    public static ChartAnalysisDefinition Create(
        int priceScale,
        IReadOnlyList<ChartZone> supportZones,
        IReadOnlyList<ChartZone> resistanceZones)
    {
        ArgumentNullException.ThrowIfNull(supportZones);
        ArgumentNullException.ThrowIfNull(resistanceZones);

        if (priceScale < 0 || priceScale > MaximumPriceScale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priceScale),
                priceScale,
                $"The price scale must be between 0 and {MaximumPriceScale}.");
        }

        if (supportZones.Count == 0 && resistanceZones.Count == 0)
        {
            throw new DomainRuleViolationException(
                ChartAnalysisDefinitionRule.MissingZones,
                "A chart-analysis definition requires at least one zone.");
        }

        IReadOnlyList<ChartZone> support = CopyZones(supportZones, nameof(supportZones));
        IReadOnlyList<ChartZone> resistance = CopyZones(resistanceZones, nameof(resistanceZones));

        EnsureConditionOrder(support, SupportConditionOrder, "A support zone");
        EnsureConditionOrder(resistance, ResistanceConditionOrder, "A resistance zone");
        EnsureUniqueZoneIds(support, resistance);
        EnsurePriceScale(support, resistance, priceScale);
        EnsureNoOverlap(support, resistance);

        return new ChartAnalysisDefinition(priceScale, support, resistance);
    }

    private static IReadOnlyList<ChartZone> CopyZones(
        IReadOnlyList<ChartZone> zones,
        string parameterName)
    {
        foreach (ChartZone zone in zones)
        {
            ArgumentNullException.ThrowIfNull(zone, parameterName);
        }

        return zones.ToArray();
    }

    private static void EnsureConditionOrder(
        IReadOnlyList<ChartZone> zones,
        ChartConditionType[] requiredOrder,
        string zoneDescription)
    {
        foreach (ChartZone zone in zones)
        {
            ChartConditionType[] actual = zone.Conditions
                .Select(condition => condition.Type)
                .ToArray();

            if (actual.SequenceEqual(requiredOrder) is false)
            {
                throw new DomainRuleViolationException(
                    ChartAnalysisDefinitionRule.InvalidConditionOrder,
                    $"{zoneDescription} must declare conditions in the order: {string.Join(", ", requiredOrder)}.");
            }
        }
    }

    private static void EnsureUniqueZoneIds(
        IReadOnlyList<ChartZone> supportZones,
        IReadOnlyList<ChartZone> resistanceZones)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (ChartZone zone in supportZones.Concat(resistanceZones))
        {
            if (seen.Add(zone.Id.Value) is false)
            {
                throw new DomainRuleViolationException(
                    ChartAnalysisDefinitionRule.DuplicateZoneId,
                    $"The zone identifier '{zone.Id.Value}' is duplicated.");
            }
        }
    }

    private static void EnsurePriceScale(
        IReadOnlyList<ChartZone> supportZones,
        IReadOnlyList<ChartZone> resistanceZones,
        int priceScale)
    {
        foreach (ChartZone zone in supportZones.Concat(resistanceZones))
        {
            if (decimal.Round(zone.Lower, priceScale) != zone.Lower
                || decimal.Round(zone.Level, priceScale) != zone.Level
                || decimal.Round(zone.Upper, priceScale) != zone.Upper)
            {
                throw new DomainRuleViolationException(
                    ChartAnalysisDefinitionRule.PriceExceedsScale,
                    $"Zone '{zone.Id.Value}' contains a price with more than {priceScale} fractional digits.");
            }
        }
    }

    private static void EnsureNoOverlap(
        IReadOnlyList<ChartZone> supportZones,
        IReadOnlyList<ChartZone> resistanceZones)
    {
        ChartZone[] ordered = supportZones
            .Concat(resistanceZones)
            .OrderBy(zone => zone.Lower)
            .ThenBy(zone => zone.Id.Value, StringComparer.Ordinal)
            .ToArray();

        for (int index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].Lower <= ordered[index - 1].Upper)
            {
                throw new DomainRuleViolationException(
                    ChartAnalysisDefinitionRule.OverlappingZones,
                    $"Zone '{ordered[index].Id.Value}' overlaps or touches zone '{ordered[index - 1].Id.Value}'.");
            }
        }
    }
}
