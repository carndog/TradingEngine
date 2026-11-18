using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Application.Signals;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class SignalTrackingPersistenceTests
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
    public async Task SetAsync_ArchiveRepeatRestore_AuditsOnlyTransitionsAndPreservesEvidence()
    {
        NotificationIntentMatchRow signal = await _database.SeedAsync();
        Instant at = SignalTestDatabase.TriggeredAt.Plus(Duration.FromHours(1));
        Result<SignalTrackingState> archived = await SetAsync(signal.Id, true, 0, at);
        Result<SignalTrackingState> repeat = await SetAsync(signal.Id, true, archived.Value.Version, at);
        Result<SignalTrackingState> restored = await SetAsync(signal.Id, false, archived.Value.Version, at.PlusTicks(1));
        await using TradingEngineDbContext context = _database.CreateContext();

        Assert.That(repeat.Value, Is.EqualTo(archived.Value));
        Assert.That(restored.Value.Archived, Is.False);
        Assert.That(restored.Value.Version, Is.GreaterThan(archived.Value.Version));
        Assert.That(await context.SignalTrackingAudit.CountAsync(row => row.SignalId == signal.Id), Is.EqualTo(2));
        Assert.That((await context.NotificationIntentMatches.SingleAsync(row => row.Id == signal.Id)).CurrentPrice, Is.EqualTo(100m));
        Assert.That((await context.NotificationIntents.SingleAsync(row => row.Id == signal.Intent.Id)).Status, Is.EqualTo("Pending"));
        Assert.That(await context.NotificationAttempts.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task SetAsync_TwoConcurrentWriters_OnlyOneTransitionWins()
    {
        NotificationIntentMatchRow signal = await _database.SeedAsync();
        Instant at = SignalTestDatabase.TriggeredAt.Plus(Duration.FromHours(1));

        Result<SignalTrackingState>[] results = await Task.WhenAll(SetAsync(signal.Id, true, 0, at), SetAsync(signal.Id, true, 0, at));
        await using TradingEngineDbContext context = _database.CreateContext();

        Assert.That(results.Count(result => result.IsSuccess), Is.EqualTo(1));
        Assert.That(results.Single(result => result.IsFailure).Error, Is.EqualTo(ChangeSignalTrackingHandler.StaleToken));
        Assert.That(await context.SignalTrackingAudit.CountAsync(row => row.SignalId == signal.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task ReadAsync_ArchiveAfterCutoff_DoesNotChangeEarlierMembership()
    {
        NotificationIntentMatchRow signal = await _database.SeedAsync();
        Instant at = SignalTestDatabase.TriggeredAt.Plus(Duration.FromHours(1));
        await SetAsync(signal.Id, true, 0, at);
        await using TradingEngineDbContext context = _database.CreateContext();
        SqlServerSignalEvidenceReader reader = new(context);

        IReadOnlyList<SignalEvidence> before = await reader.ReadAsync(new SignalQuery(at.PlusTicks(-1), SignalId: signal.Id, Archived: false), CancellationToken.None);
        IReadOnlyList<SignalEvidence> after = await reader.ReadAsync(new SignalQuery(at, SignalId: signal.Id, Archived: true, TrackingChangedFrom: at), CancellationToken.None);
        IReadOnlyList<SignalEvidence> excluded = await reader.ReadAsync(new SignalQuery(at, SignalId: signal.Id, Archived: false), CancellationToken.None);

        Assert.That(before.Single().Archived, Is.False);
        Assert.That(after.Single().Archived, Is.True);
        Assert.That(after.Single().TrackingChangedAt, Is.EqualTo(at.ToDateTimeOffset()));
        Assert.That(excluded, Is.Empty);
    }

    [Test]
    public async Task SetAsync_UnknownSignal_DoesNotCreateOrphanAudit()
    {
        Result<SignalTrackingState> result = await SetAsync(Guid.NewGuid(), true, 0, SignalTestDatabase.TriggeredAt);
        Assert.That(result.Error, Is.EqualTo(ReadSignalsHandler.NotFound));
    }

    private async Task<Result<SignalTrackingState>> SetAsync(Guid id, bool archived, long version, Instant at)
    {
        await using TradingEngineDbContext context = _database.CreateContext();
        return await new SqlServerSignalTrackingStore(context).SetAsync(id, archived, version, "synthetic-actor", at, CancellationToken.None);
    }
}
