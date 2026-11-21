using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class SignalQuoteLookupMigrationTests
{
    [Test]
    public async Task MigrateAsync_ExistingPrices_PopulatesObservedTimeWithoutChangingEvidence()
    {
        await using SignalTestDatabase database = new();
        await database.StartAsync("20261118010853_WeeklyReportSnapshots");
        NotificationIntentRow intent = await database.SeedIntentAsync();
        Guid instrumentId = intent.WatchedInstrumentId;
        Guid signalId = Guid.NewGuid();
        Guid providerQuoteId = Guid.NewGuid();
        Guid fallbackQuoteId = Guid.NewGuid();
        Instant firstSlot = SignalTestDatabase.TriggeredAt.Plus(Duration.FromMinutes(15));
        Instant secondSlot = firstSlot.Plus(Duration.FromMinutes(15));
        Instant providerAt = firstSlot.Minus(Duration.FromMinutes(1));
        await using TradingEngineDbContext context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO NotificationIntentMatches
                (Id, NotificationIntentId, ZoneId, Condition, ActionId, Lower, Level, Upper, PreviousPrice, CurrentPrice)
            VALUES ({signalId}, {intent.Id}, N'synthetic-zone', N'BuyZone', N'notify', 95, 100, 105, 106, 103)
            """);
        await InsertAsync(context, providerQuoteId, instrumentId, firstSlot, providerAt);
        await InsertAsync(context, fallbackQuoteId, instrumentId, secondSlot, null);

        await context.Database.MigrateAsync();
        List<PriceObservationRow> prices = await context.PriceObservations.AsNoTracking()
            .Where(row => row.WatchedInstrumentId == instrumentId).OrderBy(row => row.ScheduledFor).ToListAsync();
        List<InstrumentSignalQuote> latest = await SignalQuoteQuery.Latest(context, [instrumentId], secondSlot).ToListAsync();

        Assert.That(prices.Select(row => row.Id), Is.EqualTo(new[] { providerQuoteId, fallbackQuoteId }));
        Assert.That(prices.Select(row => row.EffectiveObservedAt), Is.EqualTo(new[] { providerAt, secondSlot }));
        Assert.That(prices.Select(row => row.ScheduledFor), Is.EqualTo(new[] { firstSlot, secondSlot }));
        Assert.That(prices.Select(row => row.RetrievedAt), Is.EqualTo(new[] { firstSlot, secondSlot }));
        Assert.That(prices.Select(row => row.Bid), Is.All.EqualTo(100m));
        Assert.That(prices.Select(row => row.Ask), Is.All.EqualTo(101m));
        Assert.That(latest.Single().Quote.Id, Is.EqualTo(fallbackQuoteId));
        NotificationIntentMatchRow migrated = await context.NotificationIntentMatches.SingleAsync();
        Assert.That(migrated.Id, Is.EqualTo(signalId));
        Assert.That(migrated.SuppressedAsRepeat, Is.False);
    }

    private static Task<int> InsertAsync(TradingEngineDbContext context, Guid id, Guid instrumentId, Instant slot, Instant? providerAt)
    {
        DateTime at = slot.ToDateTimeUtc();
        DateTime? providerTime = providerAt?.ToDateTimeUtc();
        Guid invocationId = Guid.NewGuid();
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PriceObservations
                (Id, WatchedInstrumentId, ScheduledFor, Provider, ProviderInstrumentId,
                 Bid, Ask, QuoteCurrency, ProviderObservedAt, RetrievedAt, Timeliness, InvocationId)
            VALUES ({id}, {instrumentId}, {at}, N'synthetic', 1, 100, 101, N'USD',
                    {providerTime}, {at}, N'Realtime', {invocationId})
            """);
    }
}
