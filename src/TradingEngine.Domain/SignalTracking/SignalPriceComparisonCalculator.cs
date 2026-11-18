using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Domain.SignalTracking;

public sealed class SignalPriceComparisonCalculator
{
    public SignalPriceComparison Compare(SignalPriceBaseline baseline, PriceObservation? latest)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        if (Enum.IsDefined(baseline.PriceBasis) is false)
        {
            return SignalPriceComparison.Unavailable(SignalComparisonUnavailableReason.InvalidPriceBasis);
        }

        if (baseline.TriggerPrice <= 0m)
        {
            return SignalPriceComparison.Unavailable(SignalComparisonUnavailableReason.InvalidTriggerPrice);
        }

        if (baseline.Lower <= 0m || baseline.Upper <= baseline.Lower)
        {
            return SignalPriceComparison.Unavailable(SignalComparisonUnavailableReason.InvalidOriginalRange);
        }

        if (string.IsNullOrWhiteSpace(baseline.QuoteCurrency))
        {
            return SignalPriceComparison.Unavailable(SignalComparisonUnavailableReason.MissingQuoteCurrency);
        }

        if (latest is null || latest.ObservedAt <= baseline.TriggeredAt)
        {
            return SignalPriceComparison.Unavailable(SignalComparisonUnavailableReason.NoLaterObservation);
        }

        if (string.Equals(baseline.QuoteCurrency, latest.QuoteCurrency, StringComparison.Ordinal) is false)
        {
            return SignalPriceComparison.Unavailable(SignalComparisonUnavailableReason.QuoteCurrencyMismatch);
        }

        decimal? latestPrice = baseline.PriceBasis == PriceBasis.Bid ? latest.Bid : latest.Ask;
        if (latestPrice is null)
        {
            return SignalPriceComparison.Unavailable(SignalComparisonUnavailableReason.MissingRequiredQuoteSide);
        }

        decimal percentageChange;
        try
        {
            percentageChange = CalculatePercentage(baseline.TriggerPrice, latestPrice.Value);
        }
        catch (OverflowException)
        {
            return SignalPriceComparison.Unavailable(SignalComparisonUnavailableReason.PercentageOutOfRange);
        }

        return SignalPriceComparison.Available(
            latestPrice.Value,
            percentageChange,
            ClassifyRange(baseline, latestPrice.Value));
    }

    private static decimal CalculatePercentage(decimal triggerPrice, decimal latestPrice)
    {
        decimal change = latestPrice - triggerPrice;
        if (Math.Abs(change) <= decimal.MaxValue / 100m)
        {
            return change * 100m / triggerPrice;
        }

        return change / triggerPrice * 100m;
    }

    private static SignalRangePosition ClassifyRange(SignalPriceBaseline baseline, decimal latestPrice)
    {
        if (latestPrice < baseline.Lower)
        {
            return SignalRangePosition.Below;
        }

        return latestPrice > baseline.Upper ? SignalRangePosition.Above : SignalRangePosition.Within;
    }
}
