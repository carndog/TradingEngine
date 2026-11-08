using TradingEngine.Domain.Results;

namespace TradingEngine.Domain.MonitoringRules;

public static class ChartEvaluationErrors
{
    public static readonly Error RevisionIdRequired = Error.Validation(
        "chart_evaluation.revision_id_required",
        "A monitoring-rule revision identifier is required.");

    public static readonly Error PriceBasisUndefined = Error.Validation(
        "chart_evaluation.price_basis_undefined",
        "The price basis is not defined.");

    public static readonly Error ObservationsOutOfOrder = Error.Validation(
        "chart_evaluation.observations_out_of_order",
        "The previous observation must be earlier than the current observation.");

    public static readonly Error QuoteCurrencyMismatch = Error.Validation(
        "chart_evaluation.quote_currency_mismatch",
        "The previous and current observations must share the same quote currency.");

    public static readonly Error QuoteCurrencyRequired = Error.Validation(
        "chart_evaluation.quote_currency_required",
        "A price observation requires a quote currency.");

    public static readonly Error QuoteSideRequired = Error.Validation(
        "chart_evaluation.quote_side_required",
        "A price observation requires at least one of bid or ask.");

    public static Error PriceNotPositive(string side)
    {
        return Error.Validation(
            "chart_evaluation.price_not_positive",
            $"An observation {side} price must be greater than zero.");
    }

    public static Error QuoteSideMissing(string subject, PriceBasis basis)
    {
        return Error.Validation(
            "chart_evaluation.quote_side_missing",
            $"The {subject} observation does not provide the {SideName(basis)} price required by the selected price basis.");
    }

    private static string SideName(PriceBasis basis)
    {
        return basis == PriceBasis.Bid ? "bid" : "ask";
    }
}
