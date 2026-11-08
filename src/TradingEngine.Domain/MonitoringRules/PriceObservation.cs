using NodaTime;
using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.MonitoringRules;

public sealed record PriceObservation
{
    private PriceObservation(Instant observedAt, decimal? bid, decimal? ask, string quoteCurrency)
    {
        ObservedAt = observedAt;
        Bid = bid;
        Ask = ask;
        QuoteCurrency = quoteCurrency;
    }

    public Instant ObservedAt { get; }

    public decimal? Bid { get; }

    public decimal? Ask { get; }

    public string QuoteCurrency { get; }

    public static Result<PriceObservation> Create(
        Instant observedAt,
        decimal? bid,
        decimal? ask,
        string? quoteCurrency)
    {
        if (string.IsNullOrWhiteSpace(quoteCurrency))
        {
            return ChartEvaluationErrors.QuoteCurrencyRequired;
        }

        if (bid is <= 0m)
        {
            return ChartEvaluationErrors.PriceNotPositive("bid");
        }

        if (ask is <= 0m)
        {
            return ChartEvaluationErrors.PriceNotPositive("ask");
        }

        if (bid is null && ask is null)
        {
            return ChartEvaluationErrors.QuoteSideRequired;
        }

        return new PriceObservation(observedAt, bid, ask, quoteCurrency.Trim());
    }
}
