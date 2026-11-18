namespace TradingEngine.Domain.SignalTracking;

public sealed record SignalPriceComparison
{
    private SignalPriceComparison(
        decimal? latestPrice,
        decimal? percentageChange,
        SignalRangePosition? rangePosition,
        SignalComparisonUnavailableReason? unavailableReason)
    {
        LatestPrice = latestPrice;
        PercentageChange = percentageChange;
        RangePosition = rangePosition;
        UnavailableReason = unavailableReason;
    }

    public decimal? LatestPrice { get; }

    public decimal? PercentageChange { get; }

    public SignalRangePosition? RangePosition { get; }

    public SignalComparisonUnavailableReason? UnavailableReason { get; }

    public bool IsAvailable => UnavailableReason is null;

    internal static SignalPriceComparison Available(
        decimal latestPrice,
        decimal percentageChange,
        SignalRangePosition rangePosition)
    {
        return new SignalPriceComparison(latestPrice, percentageChange, rangePosition, null);
    }

    internal static SignalPriceComparison Unavailable(SignalComparisonUnavailableReason reason)
    {
        return new SignalPriceComparison(null, null, null, reason);
    }
}
