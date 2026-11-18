using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Signals;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.SignalTracking;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerSignalEvidenceReader(TradingEngineDbContext context) : ISignalEvidenceReader
{
    public async Task<IReadOnlyList<SignalEvidence>> ReadAsync(SignalQuery query, CancellationToken cancellationToken)
    {
        IQueryable<NotificationIntentMatchRow> matches = context.NotificationIntentMatches.AsNoTracking()
            .Where(row => row.Intent.ScheduledFor <= query.Cutoff && row.Intent.CreatedAt <= query.Cutoff);
        if (query.InstrumentId is not null)
        {
            matches = matches.Where(row => row.Intent.WatchedInstrumentId == query.InstrumentId);
        }

        if (query.SignalId is not null)
        {
            matches = matches.Where(row => row.Id == query.SignalId);
        }

        if (query.TriggeredFrom is not null)
        {
            matches = matches.Where(row => row.Intent.ScheduledFor >= query.TriggeredFrom);
        }

        if (query.TriggeredTo is not null)
        {
            matches = matches.Where(row => row.Intent.ScheduledFor < query.TriggeredTo);
        }

        List<NotificationIntentMatchRow> rows = await matches
            .Include(row => row.Intent).ThenInclude(intent => intent.Instrument)
            .OrderByDescending(row => row.Intent.ScheduledFor).ThenBy(row => row.Id)
            .Skip(checked((query.Page - 1) * query.PageSize)).Take(query.PageSize + 1)
            .ToListAsync(cancellationToken);
        Guid[] instrumentIds = rows.Select(row => row.Intent.WatchedInstrumentId).Distinct().ToArray();
        Guid[] revisionIds = rows.Select(row => row.Intent.MonitoringRuleRevisionId).Distinct().ToArray();
        if (rows.Count == 0)
        {
            return [];
        }

        IQueryable<PriceObservationRow> eligible = context.PriceObservations.AsNoTracking()
            .Where(row => row.ScheduledFor <= query.Cutoff && row.RetrievedAt <= query.Cutoff
                && (row.ProviderObservedAt == null || row.ProviderObservedAt <= query.Cutoff));
        List<PriceObservationRow> quotes = await context.WatchedInstruments.AsNoTracking()
            .Where(instrument => instrumentIds.Contains(instrument.Id))
            .SelectMany(instrument => eligible.Where(row => row.WatchedInstrumentId == instrument.Id)
                .OrderByDescending(row => row.ProviderObservedAt ?? row.RetrievedAt)
                .ThenByDescending(row => row.ScheduledFor).ThenByDescending(row => row.RetrievedAt)
                .ThenBy(row => row.Id).Take(1))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, PriceObservationRow> byInstrument = quotes.ToDictionary(row => row.WatchedInstrumentId);
        List<Guid> activeRevisions = await context.MonitoringRuleRevisions.AsNoTracking()
            .Where(row => revisionIds.Contains(row.Id) && row.EffectiveFrom != null
                && row.EffectiveFrom <= query.Cutoff && (row.EffectiveTo == null || row.EffectiveTo > query.Cutoff))
            .Select(row => row.Id).ToListAsync(cancellationToken);

        return rows.Select(row => Map(row, byInstrument.GetValueOrDefault(row.Intent.WatchedInstrumentId),
            activeRevisions.Contains(row.Intent.MonitoringRuleRevisionId))).ToArray();
    }

    private static SignalEvidence Map(NotificationIntentMatchRow row, PriceObservationRow? quote, bool revisionActive)
    {
        NotificationIntentRow intent = row.Intent;
        PriceBasis basis = Enum.TryParse(intent.PriceBasis, out PriceBasis parsed) ? parsed : (PriceBasis)(-1);
        return new SignalEvidence(row.Id, intent.Id, intent.WatchedInstrumentId, intent.Instrument.Symbol,
            intent.Instrument.Exchange, intent.MonitoringRuleRevisionId, row.ZoneId, row.Condition, row.ActionId,
            new SignalPriceBaseline(intent.ScheduledFor, row.CurrentPrice, basis, intent.QuoteCurrency, row.Lower, row.Upper),
            intent.Status, intent.Instrument.MonitoringState != "Monitored", intent.Instrument.DeletedAt is not null,
            revisionActive, intent.Instrument.SamplingIntervalSeconds,
            quote is null ? null : new SignalQuote(quote.Id, quote.ScheduledFor, quote.RetrievedAt,
                quote.ProviderObservedAt, quote.Bid, quote.Ask, quote.QuoteCurrency));
    }
}
