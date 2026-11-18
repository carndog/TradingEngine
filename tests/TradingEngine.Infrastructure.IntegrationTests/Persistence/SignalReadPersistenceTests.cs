using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.Signals;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class SignalReadPersistenceTests
{
    private SignalTestDatabase _database = null!;

    [OneTimeSetUp]
    public async Task SetUp()
    {
        _database = new SignalTestDatabase();
        await _database.StartAsync();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        await _database.DisposeAsync();
    }

    [Test]
    public async Task ReadAsync_Cutoff_ExcludesLateRetrievalFutureQuoteAndFutureSlot()
    {
        NotificationIntentMatchRow match = await _database.SeedAsync();
        Guid instrumentId = match.Intent.WatchedInstrumentId;
        Instant cutoff = SignalTestDatabase.TriggeredAt.Plus(Duration.FromHours(2));
        PriceObservationRow expected = await _database.QuoteAsync(instrumentId, cutoff.Minus(Duration.FromMinutes(45)));
        await _database.QuoteAsync(instrumentId, cutoff.Minus(Duration.FromMinutes(30)), retrievedAt: cutoff.PlusTicks(1));
        await _database.QuoteAsync(instrumentId, cutoff.Minus(Duration.FromMinutes(15)), providerAt: cutoff.PlusTicks(1));
        await _database.QuoteAsync(instrumentId, cutoff.Plus(Duration.FromMinutes(15)), retrievedAt: cutoff);
        await using TradingEngineDbContext context = _database.CreateContext();

        IReadOnlyList<SignalEvidence> found = await new SqlServerSignalEvidenceReader(context)
            .ReadAsync(new SignalQuery(cutoff, instrumentId), CancellationToken.None);

        Assert.That(found.Single().Quote!.Id, Is.EqualTo(expected.Id));
        Assert.That(context.ChangeTracker.Entries(), Is.Empty);
        Assert.That(await context.NotificationAttempts.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task ReadAsync_MultipleMatchesAndLaterReentry_PreserveSeparateIdsWithBoundedPages()
    {
        NotificationIntentMatchRow first = await _database.SeedAsync();
        await using TradingEngineDbContext setup = _database.CreateContext();
        NotificationIntentRow original = await setup.NotificationIntents.SingleAsync(row => row.Id == first.Intent.Id);
        NotificationIntentMatchRow second = SignalTestDatabase.Match(original);
        NotificationIntentRow later = new()
        {
            Id = Guid.NewGuid(), WatchedInstrumentId = original.WatchedInstrumentId,
            ScheduledFor = original.ScheduledFor.Plus(Duration.FromMinutes(15)), MonitoringRuleRevisionId = original.MonitoringRuleRevisionId,
            Reason = "ConditionsTriggered", PriceBasis = "Bid", QuoteCurrency = "USD", Status = "Failed",
            CreatedAt = original.CreatedAt.Plus(Duration.FromMinutes(15)), UpdatedAt = original.UpdatedAt
        };
        NotificationIntentMatchRow reentry = SignalTestDatabase.Match(later);
        setup.AddRange(second, reentry);
        await setup.SaveChangesAsync();
        Instant cutoff = SignalTestDatabase.TriggeredAt.Plus(Duration.FromDays(1));
        await using TradingEngineDbContext context = _database.CreateContext();
        SqlServerSignalEvidenceReader reader = new(context);

        IReadOnlyList<SignalEvidence> page1 = await reader.ReadAsync(new SignalQuery(cutoff, original.WatchedInstrumentId, PageSize: 1), CancellationToken.None);
        IReadOnlyList<SignalEvidence> page2 = await reader.ReadAsync(new SignalQuery(cutoff, original.WatchedInstrumentId, Page: 2, PageSize: 1), CancellationToken.None);
        IReadOnlyList<SignalEvidence> page3 = await reader.ReadAsync(new SignalQuery(cutoff, original.WatchedInstrumentId, Page: 3, PageSize: 1), CancellationToken.None);

        Assert.That(page1, Has.Count.EqualTo(2));
        Assert.That(page1[0].SignalId, Is.EqualTo(reentry.Id));
        Assert.That(new[] { page1[0].SignalId, page2[0].SignalId, page3[0].SignalId }, Is.EquivalentTo(new[] { first.Id, second.Id, reentry.Id }));
        Assert.That(page1[0].EmailStatus, Is.EqualTo("Failed"));
    }

    [Test]
    public async Task ReadAsync_DeletedPausedAndSuperseded_PreservesOriginalEvidence()
    {
        NotificationIntentMatchRow match = await _database.SeedAsync();
        await using TradingEngineDbContext setup = _database.CreateContext();
        WatchedInstrumentRow instrument = await setup.WatchedInstruments.SingleAsync(row => row.Id == match.Intent.WatchedInstrumentId);
        instrument.DeletedAt = SignalTestDatabase.TriggeredAt.Plus(Duration.FromMinutes(1));
        instrument.MonitoringState = "Configured";
        MonitoringRuleRevisionRow revision = await setup.MonitoringRuleRevisions.SingleAsync(row => row.Id == match.Intent.MonitoringRuleRevisionId);
        revision.EffectiveTo = SignalTestDatabase.TriggeredAt.Plus(Duration.FromMinutes(2));
        await setup.SaveChangesAsync();
        await using TradingEngineDbContext context = _database.CreateContext();

        SignalEvidence found = (await new SqlServerSignalEvidenceReader(context).ReadAsync(
            new SignalQuery(SignalTestDatabase.TriggeredAt.Plus(Duration.FromHours(1)), SignalId: match.Id), CancellationToken.None)).Single();

        Assert.That(found.InstrumentRemoved, Is.True);
        Assert.That(found.MonitoringPaused, Is.True);
        Assert.That(found.SourceRevisionActive, Is.False);
        Assert.That(found.Baseline.TriggerPrice, Is.EqualTo(100m));
        Assert.That(found.Baseline.Lower, Is.EqualTo(95m));
        Assert.That(found.Baseline.Upper, Is.EqualTo(105m));
    }

    [Test]
    public async Task ReadAsync_LatestMissingBid_DoesNotUseOlderBid()
    {
        NotificationIntentMatchRow match = await _database.SeedAsync();
        Instant cutoff = SignalTestDatabase.TriggeredAt.Plus(Duration.FromHours(1));
        await _database.QuoteAsync(match.Intent.WatchedInstrumentId, cutoff.Minus(Duration.FromMinutes(30)));
        PriceObservationRow latest = await _database.QuoteAsync(match.Intent.WatchedInstrumentId, cutoff.Minus(Duration.FromMinutes(15)), bid: null);
        await using TradingEngineDbContext context = _database.CreateContext();

        SignalEvidence found = (await new SqlServerSignalEvidenceReader(context).ReadAsync(new SignalQuery(cutoff, SignalId: match.Id), CancellationToken.None)).Single();

        Assert.That(found.Quote!.Id, Is.EqualTo(latest.Id));
        Assert.That(found.Quote.Bid, Is.Null);
    }
}
