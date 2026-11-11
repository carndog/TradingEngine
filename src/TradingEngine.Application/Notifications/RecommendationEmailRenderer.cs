using System.Globalization;
using System.Text;
using NodaTime.Text;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Application.Notifications;

public sealed class RecommendationEmailRenderer
{
    public RecommendationEmail Render(PendingNotification intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        string subject = string.Create(
            CultureInfo.InvariantCulture,
            $"TradingEngine recommendation: {intent.Symbol} ({intent.Exchange}) " +
            $"{DescribeConditions(intent.Matches)} at {intent.ScheduledFor.InUtc().Date:yyyy-MM-dd}");

        StringBuilder body = new();
        body.AppendLine("TradingEngine chart recommendation");
        body.AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture, $"Instrument: {intent.Symbol} ({intent.Exchange})");
        body.AppendLine(CultureInfo.InvariantCulture, $"Observation time (UTC): {InstantPattern.ExtendedIso.Format(intent.ScheduledFor)}");
        body.AppendLine(CultureInfo.InvariantCulture, $"Observed price: {FormatBasisPrice(intent)}");
        body.AppendLine(CultureInfo.InvariantCulture, $"Bid / Ask: {FormatNullable(intent.CurrentBid)} / {FormatNullable(intent.CurrentAsk)} {intent.QuoteCurrency}");
        body.AppendLine("Evaluated range:");
        foreach (TriggeredCondition match in intent.Matches)
        {
            body.AppendLine(CultureInfo.InvariantCulture,
                $"  zone {match.ZoneId.Value}: {FormatDecimal(match.Lower)} - {FormatDecimal(match.Upper)} " +
                $"(level {FormatDecimal(match.Level)}); {Describe(match.Condition)} on " +
                $"{FormatDecimal(match.PreviousPrice)} -> {FormatDecimal(match.CurrentPrice)} " +
                $"(action {match.ActionId.Value})");
        }

        body.AppendLine(CultureInfo.InvariantCulture, $"Reason: {DescribeReason(intent)}");
        body.AppendLine(CultureInfo.InvariantCulture, $"Monitoring-rule revision: {intent.MonitoringRuleRevisionId}");
        body.AppendLine();
        body.AppendLine("This is an informational recommendation only; no order was placed.");

        return new RecommendationEmail(subject, body.ToString());
    }

    private static string FormatBasisPrice(PendingNotification intent)
    {
        decimal? price = intent.PriceBasis == PriceBasis.Ask
            ? intent.CurrentAsk
            : intent.CurrentBid;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatNullable(price)} {intent.QuoteCurrency} ({intent.PriceBasis} basis)");
    }

    private static string DescribeReason(PendingNotification intent)
    {
        return intent.Matches.Count == 1
            ? "A configured chart condition crossed between the previous and current observations."
            : "Configured chart conditions crossed between the previous and current observations.";
    }

    private static string DescribeConditions(IReadOnlyList<TriggeredCondition> matches)
    {
        return matches.Count == 0
            ? "chart condition"
            : Describe(matches[0].Condition);
    }

    private static string Describe(ChartConditionType condition)
    {
        return condition switch
        {
            ChartConditionType.BuyZone => "buy-zone entry",
            ChartConditionType.SupportLoss => "support-loss",
            ChartConditionType.Breakout => "breakout",
            _ => condition.ToString()
        };
    }

    private static string FormatNullable(decimal? value)
    {
        return value is null
            ? "n/a"
            : FormatDecimal(value.Value);
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
