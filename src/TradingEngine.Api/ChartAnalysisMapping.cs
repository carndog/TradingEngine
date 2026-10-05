using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api;

internal static class ChartAnalysisMapping
{
    internal static Result<ChartAnalysisDefinition> ToDefinition(
        int priceScale,
        IReadOnlyList<ChartZoneDto>? supportZones,
        IReadOnlyList<ChartZoneDto>? resistanceZones)
    {
        Result<ChartZone[]> support = MapZones(supportZones);
        if (support.IsFailure)
        {
            return support.Error;
        }

        Result<ChartZone[]> resistance = MapZones(resistanceZones);
        if (resistance.IsFailure)
        {
            return resistance.Error;
        }

        return ChartAnalysisDefinition.Create(
            priceScale,
            support.Value,
            resistance.Value);
    }

    internal static MonitoringRuleDefinitionDto ToDto(ChartAnalysisDefinition definition)
    {
        return new MonitoringRuleDefinitionDto(
            definition.PriceScale,
            definition.SupportZones.Select(ToDto).ToArray(),
            definition.ResistanceZones.Select(ToDto).ToArray());
    }

    private static Result<ChartZone[]> MapZones(IReadOnlyList<ChartZoneDto>? zones)
    {
        if (zones is null)
        {
            return Result<ChartZone[]>.Success([]);
        }

        List<ChartZone> mapped = new(zones.Count);
        foreach (ChartZoneDto zone in zones)
        {
            Result<ChartZone> result = MapZone(zone);
            if (result.IsFailure)
            {
                return result.Error;
            }

            mapped.Add(result.Value);
        }

        return mapped.ToArray();
    }

    private static Result<ChartZone> MapZone(ChartZoneDto? zone)
    {
        if (zone is null)
        {
            return ChartAnalysisErrors.ZoneRequired;
        }

        Result<ChartAnalysisIdentifier> id = ChartAnalysisIdentifier.From(zone.Id);
        if (id.IsFailure)
        {
            return id.Error;
        }

        Result<ChartCondition[]> conditions = MapConditions(zone.Conditions);
        if (conditions.IsFailure)
        {
            return conditions.Error;
        }

        return ChartZone.Create(id.Value, zone.Lower, zone.Level, zone.Upper, conditions.Value);
    }

    private static Result<ChartCondition[]> MapConditions(IReadOnlyList<ChartConditionDto>? conditions)
    {
        if (conditions is null)
        {
            return Result<ChartCondition[]>.Success([]);
        }

        List<ChartCondition> mapped = new(conditions.Count);
        foreach (ChartConditionDto condition in conditions)
        {
            Result<ChartCondition> result = MapCondition(condition);
            if (result.IsFailure)
            {
                return result.Error;
            }

            mapped.Add(result.Value);
        }

        return mapped.ToArray();
    }

    private static Result<ChartCondition> MapCondition(ChartConditionDto condition)
    {
        if (condition is null)
        {
            return ChartAnalysisErrors.ConditionRequired;
        }

        Result<ChartConditionType> type = MapConditionType(condition.Type);
        if (type.IsFailure)
        {
            return type.Error;
        }

        Result<ChartAnalysisIdentifier> actionId = ChartAnalysisIdentifier.From(condition.ActionId);
        if (actionId.IsFailure)
        {
            return actionId.Error;
        }

        return ChartCondition.Create(type.Value, actionId.Value);
    }

    private static Result<ChartConditionType> MapConditionType(string? value)
    {
        return value switch
        {
            "buy-zone" => ChartConditionType.BuyZone,
            "support-loss" => ChartConditionType.SupportLoss,
            "breakout" => ChartConditionType.Breakout,
            _ => ChartAnalysisErrors.ConditionTypeUndefined
        };
    }

    private static ChartZoneDto ToDto(ChartZone zone)
    {
        return new ChartZoneDto(
            zone.Id.Value,
            zone.Lower,
            zone.Level,
            zone.Upper,
            zone.Conditions.Select(ToDto).ToArray());
    }

    private static ChartConditionDto ToDto(ChartCondition condition)
    {
        return new ChartConditionDto(
            FormatConditionType(condition.Type),
            condition.ActionId.Value);
    }

    private static string FormatConditionType(ChartConditionType type)
    {
        return type switch
        {
            ChartConditionType.BuyZone => "buy-zone",
            ChartConditionType.SupportLoss => "support-loss",
            ChartConditionType.Breakout => "breakout",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown chart condition type.")
        };
    }
}
