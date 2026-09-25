using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.MonitoringRules;

public static class ChartAnalysisErrors
{
    public static readonly Error IdentifierRequired = Error.Validation(
        "chart_analysis.identifier_required",
        "A chart-analysis identifier is required.");

    public static readonly Error IdentifierExceedsMaximumLength = Error.Validation(
        "chart_analysis.identifier_exceeds_maximum_length",
        "A chart-analysis identifier cannot exceed 64 characters.");

    public static readonly Error IdentifierInvalidCharacters = Error.Validation(
        "chart_analysis.identifier_invalid_characters",
        "A chart-analysis identifier must start with a lowercase ASCII letter and contain only lowercase ASCII letters, digits, periods and hyphens.");

    public static readonly Error ZoneNonPositivePrice = Error.Validation(
        "chart_analysis.zone_non_positive_price",
        "Zone prices must be greater than zero.");

    public static readonly Error ZoneInvalidBoundaryOrder = Error.Validation(
        "chart_analysis.zone_invalid_boundary_order",
        "A zone must satisfy lower < level < upper.");

    public static readonly Error ZoneMissingConditions = Error.Validation(
        "chart_analysis.zone_missing_conditions",
        "A zone requires at least one condition.");

    public static readonly Error ConditionTypeUndefined = Error.Validation(
        "chart_analysis.condition_type_undefined",
        "Unknown chart condition type.");

    public static readonly Error ZoneRequired = Error.Validation(
        "chart_analysis.zone_required",
        "A zone entry cannot be null.");

    public static readonly Error ConditionRequired = Error.Validation(
        "chart_analysis.condition_required",
        "A zone condition entry cannot be null.");

    public static readonly Error MissingZones = Error.Validation(
        "chart_analysis.missing_zones",
        "A chart-analysis definition requires at least one zone.");

    public static readonly Error PriceScaleOutOfRange = Error.Validation(
        "chart_analysis.price_scale_out_of_range",
        "The price scale must be between 0 and 8.");

    public static Error InvalidConditionOrder(
        string zoneDescription,
        IReadOnlyList<ChartConditionType> requiredOrder)
    {
        return Error.Validation(
            "chart_analysis.invalid_condition_order",
            $"{zoneDescription} must declare conditions in the order: {string.Join(", ", requiredOrder)}.");
    }

    public static Error DuplicateZoneId(string zoneId)
    {
        return Error.Validation(
            "chart_analysis.duplicate_zone_id",
            $"The zone identifier '{zoneId}' is duplicated.");
    }

    public static Error PriceExceedsScale(string zoneId, int priceScale)
    {
        return Error.Validation(
            "chart_analysis.price_exceeds_scale",
            $"Zone '{zoneId}' contains a price with more than {priceScale} fractional digits.");
    }

    public static Error OverlappingZones(string zoneId, string previousZoneId)
    {
        return Error.Validation(
            "chart_analysis.overlapping_zones",
            $"Zone '{zoneId}' overlaps or touches zone '{previousZoneId}'.");
    }
}
