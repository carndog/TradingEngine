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
        Result<ChartZone[]> support = MapZones("support", supportZones);
        if (support.IsFailure)
        {
            return support.Error;
        }

        Result<ChartZone[]> resistance = MapZones("resistance", resistanceZones);
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

    private static Result<ChartZone[]> MapZones(string kind, IReadOnlyList<ChartZoneDto>? zones)
    {
        if (zones is null)
        {
            return Result<ChartZone[]>.Success([]);
        }

        List<ChartZone> mapped = new(zones.Count);
        for (int index = 0; index < zones.Count; index++)
        {
            Result<ChartZone> result = MapZone(kind, index + 1, zones[index]);
            if (result.IsFailure)
            {
                return result.Error;
            }

            mapped.Add(result.Value);
        }

        return mapped.ToArray();
    }

    private static Result<ChartZone> MapZone(string kind, int position, ChartZoneDto? zone)
    {
        if (zone is null)
        {
            return ChartAnalysisErrors.ZoneRequired;
        }

        Result<ChartAnalysisIdentifier> id = ChartAnalysisIdentifier.From(zone.Id);
        if (id.IsFailure)
        {
            return DescribeIdentifierError(id.Error, $"The {kind} zone {position} identifier", zone.Id);
        }

        Result<ChartCondition[]> conditions = MapConditions(zone.Id!, zone.Conditions);
        if (conditions.IsFailure)
        {
            return conditions.Error;
        }

        return ChartZone.Create(id.Value, zone.Lower, zone.Level, zone.Upper, conditions.Value);
    }

    private static Result<ChartCondition[]> MapConditions(string zoneId, IReadOnlyList<ChartConditionDto>? conditions)
    {
        if (conditions is null)
        {
            return Result<ChartCondition[]>.Success([]);
        }

        List<ChartCondition> mapped = new(conditions.Count);
        foreach (ChartConditionDto? condition in conditions)
        {
            Result<ChartCondition> result = MapCondition(zoneId, condition);
            if (result.IsFailure)
            {
                return result.Error;
            }

            mapped.Add(result.Value);
        }

        return mapped.ToArray();
    }

    private static Result<ChartCondition> MapCondition(string zoneId, ChartConditionDto? condition)
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
            return DescribeIdentifierError(
                actionId.Error,
                $"The action identifier on the {condition.Type} condition of zone '{zoneId}'",
                condition.ActionId);
        }

        return ChartCondition.Create(type.Value, actionId.Value);
    }

    private static Error DescribeIdentifierError(Error error, string subject, string? value)
    {
        if (error == ChartAnalysisErrors.IdentifierRequired)
        {
            return ChartAnalysisErrors.IdentifierRequiredIn(subject);
        }

        if (error == ChartAnalysisErrors.IdentifierExceedsMaximumLength)
        {
            return ChartAnalysisErrors.IdentifierExceedsMaximumLengthIn(subject);
        }

        return ChartAnalysisErrors.IdentifierInvalidCharactersIn(subject, value);
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
