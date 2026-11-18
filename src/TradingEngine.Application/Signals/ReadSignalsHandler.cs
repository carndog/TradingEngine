using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Domain.SignalTracking;

namespace TradingEngine.Application.Signals;

public sealed class ReadSignalsHandler(
    ISignalEvidenceReader reader,
    SignalFreshnessClassifier freshness,
    IClock clock)
{
    public static Error InvalidQuery => Error.Validation("signals.invalid_query", "Supply a valid cutoff, date range and bounded page (1–100 rows).");

    public static Error NotFound => Error.NotFound("signals.not_found", "The recorded signal was not found at this cutoff.");

    public async Task<Result<SignalPage>> HandleAsync(SignalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page < 1 || query.PageSize is < 1 or > 100
            || (long)(query.Page - 1) * query.PageSize > int.MaxValue
            || PersistedInstant.IsPersistable(query.Cutoff) is false
            || query.Cutoff > clock.GetCurrentInstant()
            || (query.TrackingChangedFrom is not null && PersistedInstant.IsPersistable(query.TrackingChangedFrom.Value) is false)
            || (query.TriggeredFrom is not null && PersistedInstant.IsPersistable(query.TriggeredFrom.Value) is false)
            || (query.TriggeredTo is not null && PersistedInstant.IsPersistable(query.TriggeredTo.Value) is false)
            || (query.TriggeredFrom is not null && query.TriggeredTo is not null && query.TriggeredFrom >= query.TriggeredTo))
        {
            return InvalidQuery;
        }

        IReadOnlyList<SignalEvidence> evidence = await reader.ReadAsync(query, cancellationToken);
        SignalStatus[] items = evidence.Take(query.PageSize).Select(item => Map(item, query.Cutoff)).ToArray();
        return new SignalPage(items, query.Cutoff.ToDateTimeOffset(), query.Page, query.PageSize, evidence.Count > query.PageSize);
    }

    private SignalStatus Map(SignalEvidence signal, Instant cutoff)
    {
        SignalQuote? quote = signal.Quote;
        PriceObservation? observation = null;
        bool invalidQuote = false;
        if (quote is not null)
        {
            Result<PriceObservation> parsed = PriceObservation.Create(quote.ObservedAt, quote.Bid, quote.Ask, quote.QuoteCurrency);
            invalidQuote = parsed.IsFailure;
            if (parsed.IsSuccess)
            {
                observation = parsed.Value;
            }
        }

        SignalPriceComparison comparison = new SignalPriceComparisonCalculator().Compare(signal.Baseline, observation);
        return new SignalStatus(
            signal.SignalId, signal.AlertId, signal.InstrumentId, signal.Symbol, signal.Exchange,
            signal.RevisionId, signal.ZoneId, signal.Condition, signal.ActionId,
            signal.Baseline.TriggeredAt.ToDateTimeOffset(), signal.Baseline.TriggerPrice,
            signal.Baseline.PriceBasis.ToString(), signal.Baseline.QuoteCurrency ?? string.Empty,
            signal.Baseline.Lower, signal.Baseline.Upper, quote?.Id,
            quote?.ObservedAt.ToDateTimeOffset(), quote?.RetrievedAt.ToDateTimeOffset(),
            comparison.LatestPrice, comparison.PercentageChange, comparison.RangePosition?.ToString(),
            invalidQuote ? "InvalidLatestQuote" : comparison.UnavailableReason?.ToString(),
            FormatFreshness(freshness.Classify(signal, cutoff)), quote is null ? null : (cutoff - quote.ObservedAt).TotalSeconds,
            signal.EmailStatus, signal.MonitoringPaused, signal.InstrumentRemoved, signal.SourceRevisionActive,
            signal.Archived, signal.TrackingChangedAt);
    }

    private static string FormatFreshness(SignalFreshness value)
    {
        return value switch
        {
            SignalFreshness.Missing => "missing",
            SignalFreshness.Current => "current",
            SignalFreshness.LatestSession => "latest_session",
            SignalFreshness.StaleLastKnown => "stale_last_known",
            SignalFreshness.UnknownCadence => "unknown_cadence",
            SignalFreshness.UnknownCalendar => "unknown_calendar",
            SignalFreshness.UnknownExpectedCollection => "unknown_expected_collection",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown signal freshness.")
        };
    }
}
