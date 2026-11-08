using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.MonitoringRules;

public sealed class ChartAnalysisEvaluator
{
    public Result<EvaluationResult> Evaluate(EvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Definition);
        ArgumentNullException.ThrowIfNull(input.Current);

        if (input.MonitoringRuleRevisionId == Guid.Empty)
        {
            return ChartEvaluationErrors.RevisionIdRequired;
        }

        if (Enum.IsDefined(input.PriceBasis) is false)
        {
            return ChartEvaluationErrors.PriceBasisUndefined;
        }

        if (input.Previous is not null)
        {
            if (input.Previous.ObservedAt >= input.Current.ObservedAt)
            {
                return ChartEvaluationErrors.ObservationsOutOfOrder;
            }

            if (string.Equals(
                input.Previous.QuoteCurrency,
                input.Current.QuoteCurrency,
                StringComparison.Ordinal) is false)
            {
                return ChartEvaluationErrors.QuoteCurrencyMismatch;
            }
        }

        decimal? currentPrice = BasisPrice(input.Current, input.PriceBasis);
        if (currentPrice is null)
        {
            return ChartEvaluationErrors.QuoteSideMissing("current", input.PriceBasis);
        }

        decimal? previousPrice = null;
        if (input.Previous is not null)
        {
            previousPrice = BasisPrice(input.Previous, input.PriceBasis);
            if (previousPrice is null)
            {
                return ChartEvaluationErrors.QuoteSideMissing("previous", input.PriceBasis);
            }
        }

        ChartZone[] zones = OrderedZones(input.Definition);

        if (zones.Length == 0)
        {
            return SucceededWith(input, EvaluationOutcome.NoAction, EvaluationReason.NoConfiguredZones, []);
        }

        if (previousPrice is null)
        {
            return SucceededWith(input, EvaluationOutcome.NoAction, EvaluationReason.FirstObservation, []);
        }

        IReadOnlyList<TriggeredCondition> matches = EvaluateZones(zones, previousPrice.Value, currentPrice.Value);

        return matches.Count == 0
            ? SucceededWith(input, EvaluationOutcome.NoAction, EvaluationReason.NoConditionTriggered, matches)
            : SucceededWith(input, EvaluationOutcome.Triggered, EvaluationReason.ConditionsTriggered, matches);
    }

    private static EvaluationResult SucceededWith(
        EvaluationInput input,
        EvaluationOutcome outcome,
        EvaluationReason reason,
        IReadOnlyList<TriggeredCondition> matches)
    {
        return new EvaluationResult(
            input.MonitoringRuleRevisionId,
            input.Current.ObservedAt,
            input.Previous?.ObservedAt,
            outcome,
            reason,
            input.PriceBasis,
            input.Current.QuoteCurrency,
            input.Current.Bid,
            input.Current.Ask,
            matches);
    }

    private static ChartZone[] OrderedZones(ChartAnalysisDefinition definition)
    {
        return definition.SupportZones
            .Concat(definition.ResistanceZones)
            .OrderBy(zone => zone.Lower)
            .ThenBy(zone => zone.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static decimal? BasisPrice(PriceObservation observation, PriceBasis basis)
    {
        return basis == PriceBasis.Bid ? observation.Bid : observation.Ask;
    }

    private static IReadOnlyList<TriggeredCondition> EvaluateZones(
        IReadOnlyList<ChartZone> zones,
        decimal previousPrice,
        decimal currentPrice)
    {
        List<TriggeredCondition> matches = [];

        foreach (ChartZone zone in zones)
        {
            foreach (ChartCondition condition in zone.Conditions)
            {
                if (ConditionMatches(condition.Type, zone, previousPrice, currentPrice))
                {
                    matches.Add(new TriggeredCondition(
                        zone.Id,
                        condition.Type,
                        condition.ActionId,
                        zone.Lower,
                        zone.Level,
                        zone.Upper,
                        previousPrice,
                        currentPrice));
                }
            }
        }

        return Array.AsReadOnly(matches.ToArray());
    }

    private static bool ConditionMatches(
        ChartConditionType type,
        ChartZone zone,
        decimal previousPrice,
        decimal currentPrice)
    {
        return type switch
        {
            ChartConditionType.BuyZone =>
                previousPrice > zone.Upper && currentPrice > zone.Level && currentPrice <= zone.Upper,
            ChartConditionType.SupportLoss =>
                previousPrice >= zone.Lower && currentPrice < zone.Lower,
            ChartConditionType.Breakout =>
                previousPrice <= zone.Upper && currentPrice > zone.Upper,
            _ => false
        };
    }
}
