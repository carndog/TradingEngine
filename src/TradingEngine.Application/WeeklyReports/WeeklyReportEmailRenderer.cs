using System.Globalization;
using System.Net;
using System.Text;
using NodaTime.Text;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Signals;

namespace TradingEngine.Application.WeeklyReports;

public sealed class WeeklyReportEmailRenderer(WeeklyReportOptions options)
{
    public RecommendationEmail Render(WeeklyReportPeriod period, Guid? reportId, IReadOnlyList<SignalStatus> items,
        IReadOnlyList<string> recipients, int skippedPeriods)
    {
        string cutoff = InstantPattern.ExtendedIso.Format(period.Cutoff);
        string subject = $"Weekly signal status — {cutoff}";
        int archived = items.Count(item => item.Archived);
        int newSignals = items.Count(item => item.Archived is false && item.TriggeredAt > period.StartsAt.ToDateTimeOffset());
        string introduction = $"Weekly signal status\nCutoff: {cutoff}; reporting time zone: {options.TimeZone}.\n"
            + $"Tracked: {items.Count - archived}; new this period: {newSignals}; recently archived: {archived}.\n"
            + $"Older scheduled periods skipped: {skippedPeriods}.\n"
            + "Price change since signal is unadjusted observed price movement, not trade profit or a direction-adjusted return.\n"
            + "Quote freshness and original-range position are separate facts; no trade is automatically closed.\n";
        string link = Continuation(period, reportId);
        StringBuilder plain = new(introduction);
        StringBuilder html = new($"<h1>Weekly signal status</h1><p>{Encode(introduction).Replace("\n", "<br>", StringComparison.Ordinal)}</p>");
        int shown = 0;
        string reservedFooter = Footer(items.Count, items.Count, link);
        int remaining = options.MaxEmailBytes - Bytes(subject, plain.ToString(), html.ToString(), reservedFooter, HtmlFooter(reservedFooter, link));
        foreach (SignalStatus item in items.OrderBy(item => Section(item, period), StringComparer.Ordinal)
                     .ThenBy(item => item.Exchange, StringComparer.Ordinal).ThenBy(item => item.Symbol, StringComparer.Ordinal)
                     .ThenBy(item => item.TriggeredAt).ThenBy(item => item.SignalId))
        {
            string row = Row(item, period);
            string htmlRow = $"<section><h2>{Encode(Section(item, period))}</h2><pre>{Encode(row)}</pre></section>";
            int size = Bytes(row, htmlRow);
            if (shown >= options.MaxEmailRows || size > remaining)
            {
                break;
            }

            plain.Append(row);
            html.Append(htmlRow);
            remaining -= size;
            shown++;
        }

        string footer = Footer(shown, items.Count, link);
        plain.Append(footer);
        html.Append(HtmlFooter(footer, link));
        if (Bytes(subject, plain.ToString(), html.ToString()) > options.MaxEmailBytes)
        {
            throw new InvalidOperationException("The report header and continuation exceed MaxEmailBytes.");
        }

        return new RecommendationEmail(subject, plain.ToString(), html.ToString(), recipients.ToArray());
    }

    private static string Row(SignalStatus item, WeeklyReportPeriod period)
    {
        string percentage = item.PercentageChange?.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%";
        if (item.PercentageChange is null)
        {
            percentage = $"unavailable ({item.ComparisonUnavailableReason ?? "no comparable quote"})";
        }

        string latest = item.LatestPrice is decimal latestPrice ? FormatPrice(latestPrice) : "unavailable";
        string time = item.LatestObservedAt?.ToString("u", CultureInfo.InvariantCulture) ?? "unavailable";
        string condition = item.Condition == "ResistanceEntry" ? "resistance entry" : item.Condition;
        return $"\n{Section(item, period)} — {item.Exchange}/{item.Symbol} — {condition}\n"
            + $"Signal {item.SignalId}; triggered {item.TriggeredAt.ToString("u", CultureInfo.InvariantCulture)}\n"
            + $"Original price: {FormatPrice(item.TriggerPrice)} {item.QuoteCurrency} ({item.PriceBasis}). "
            + $"Latest saved price: {latest} {item.QuoteCurrency} at {time}.\n"
            + $"Price change since signal: {percentage}. Quote freshness: {item.QuoteFreshness}; "
            + $"age seconds: {item.QuoteAgeSeconds?.ToString("0", CultureInfo.InvariantCulture) ?? "unknown"}.\n"
            + $"Original range: {FormatPrice(item.Lower)}–{FormatPrice(item.Upper)}; "
            + $"position: {item.RangePosition ?? "unavailable"}. Original email status: {item.EmailStatus}.\n"
            + $"Archived: {item.Archived}; monitoring paused: {item.MonitoringPaused}; instrument removed: {item.InstrumentRemoved}; "
            + $"source revision active: {item.SourceRevisionActive}.\n";
    }

    private static string FormatPrice(decimal value)
    {
        decimal rounded = decimal.Round(value, 8, MidpointRounding.AwayFromZero);
        if (rounded == 0m && value != 0m)
        {
            return value.ToString("0.00##########################", CultureInfo.InvariantCulture);
        }

        return rounded.ToString("0.00######", CultureInfo.InvariantCulture);
    }

    private static string Section(SignalStatus item, WeeklyReportPeriod period)
    {
        if (item.Archived)
        {
            return "Recently archived";
        }

        return item.TriggeredAt > period.StartsAt.ToDateTimeOffset() ? "New this period" : "Continuing tracked";
    }

    private string Continuation(WeeklyReportPeriod period, Guid? reportId)
    {
        string path = reportId is null
            ? $"/api/weekly-reports/preview?cutoff={Uri.EscapeDataString(InstantPattern.ExtendedIso.Format(period.Cutoff))}&page=1&pageSize=100"
            : $"/api/weekly-reports/{reportId}?page=1&pageSize=100";
        return options.BaseUrl.TrimEnd('/') + path;
    }

    private static string Footer(int shown, int total, string link)
    {
        return $"\nShowing {shown} of {total} signal rows. View all evidence (sign-in required): {link}\n";
    }

    private static string HtmlFooter(string footer, string link)
    {
        return $"<p>{Encode(footer)}</p><p><a href=\"{Encode(link)}\">View all evidence (sign-in required)</a></p>";
    }

    private static int Bytes(params string[] values)
    {
        return values.Sum(Encoding.UTF8.GetByteCount);
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
