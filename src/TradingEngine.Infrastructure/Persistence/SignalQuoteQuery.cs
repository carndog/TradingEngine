using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.Signals;

namespace TradingEngine.Infrastructure.Persistence;

internal static class SignalQuoteQuery
{
    public static IQueryable<InstrumentSignalQuote> Latest(TradingEngineDbContext context, Guid[] instrumentIds, Instant cutoff)
    {
        return context.WatchedInstruments.AsNoTracking()
            .Where(instrument => instrumentIds.Contains(instrument.Id))
            .SelectMany(instrument => context.PriceObservations.AsNoTracking()
                .Where(row => row.WatchedInstrumentId == instrument.Id && row.EffectiveObservedAt <= cutoff
                    && row.ScheduledFor <= cutoff && row.RetrievedAt <= cutoff)
                .OrderByDescending(row => row.EffectiveObservedAt)
                .ThenByDescending(row => row.ScheduledFor).ThenByDescending(row => row.RetrievedAt)
                .ThenBy(row => row.Id)
                .Select(row => new InstrumentSignalQuote(instrument.Id,
                    new SignalQuote(row.Id, row.ScheduledFor, row.RetrievedAt, row.ProviderObservedAt, row.Bid, row.Ask, row.QuoteCurrency)))
                .Take(1));
    }
}
