using TradingEngine.Domain.Results;

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

    public static Result<ChartAnalysisDefinition> Create(
        int priceScale,
        IReadOnlyList<ChartZone> supportZones,
        IReadOnlyList<ChartZone> resistanceZones)
    {
        ArgumentNullException.ThrowIfNull(supportZones);
        ArgumentNullException.ThrowIfNull(resistanceZones);

        if (priceScale < 0 || priceScale > MaximumPriceScale)
        {
            return ChartAnalysisErrors.PriceScaleOutOfRange;
        }

        if (supportZones.Count == 0 && resistanceZones.Count == 0)
        {
            return ChartAnalysisErrors.MissingZones;
        }

        IReadOnlyList<ChartZone> support = CopyZones(supportZones, nameof(supportZones));
        IReadOnlyList<ChartZone> resistance = CopyZones(resistanceZones, nameof(resistanceZones));

        Error? failure = EnsureConditionOrder(support, SupportConditionOrder, "A support zone")
            ?? EnsureConditionOrder(resistance, ResistanceConditionOrder, "A resistance zone")
            ?? EnsureUniqueZoneIds(support, resistance)
            ?? EnsurePriceScale(support, resistance, priceScale)
            ?? EnsureNoOverlap(support, resistance);

        if (failure is not null)
        {
            return failure;
        }

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

    private static Error? EnsureConditionOrder(
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
                return ChartAnalysisErrors.InvalidConditionOrder(zoneDescription, requiredOrder);
            }
        }

        return null;
    }

    private static Error? EnsureUniqueZoneIds(
        IReadOnlyList<ChartZone> supportZones,
        IReadOnlyList<ChartZone> resistanceZones)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (ChartZone zone in supportZones.Concat(resistanceZones))
        {
            if (seen.Add(zone.Id.Value) is false)
            {
                return ChartAnalysisErrors.DuplicateZoneId(zone.Id.Value);
            }
        }

        return null;
    }

    private static Error? EnsurePriceScale(
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
                return ChartAnalysisErrors.PriceExceedsScale(zone.Id.Value, priceScale);
            }
        }

        return null;
    }

    private static Error? EnsureNoOverlap(
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
                return ChartAnalysisErrors.OverlappingZones(
                    ordered[index].Id.Value,
                    ordered[index - 1].Id.Value);
            }
        }

        return null;
    }
}
