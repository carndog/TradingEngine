namespace TradingEngine.Domain.SignalTracking;

public enum SignalComparisonUnavailableReason
{
    InvalidPriceBasis,
    InvalidTriggerPrice,
    InvalidOriginalRange,
    MissingQuoteCurrency,
    NoLaterObservation,
    QuoteCurrencyMismatch,
    MissingRequiredQuoteSide,
    PercentageOutOfRange
}
