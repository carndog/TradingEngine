using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class RepeatRangeEmailMigrationTests
{
    [Test]
    public async Task MigrateAsync_MixedIntentHasSuppressedMatch_RefusesRollbackAndPreservesDecision()
    {
        await using SignalTestDatabase database = new();
        await database.StartAsync();
        NotificationIntentMatchRow selected = await database.SeedAsync();
        await using TradingEngineDbContext context = database.CreateContext();
        NotificationIntentRow intent = await context.NotificationIntents.SingleAsync();
        NotificationIntentMatchRow suppressed = SignalTestDatabase.Match(intent);
        suppressed.ZoneId = "other-zone";
        suppressed.SuppressedAsRepeat = true;
        context.Add(suppressed);
        await context.SaveChangesAsync();

        IMigrator migrator = context.GetService<IMigrator>();
        Assert.That(async () => await migrator.MigrateAsync("20261119232945_SignalQuoteLookup"),
            Throws.TypeOf<SqlException>().With.Property(nameof(SqlException.Number)).EqualTo(51000));

        await using TradingEngineDbContext verification = database.CreateContext();
        Dictionary<Guid, bool> decisions = await verification.NotificationIntentMatches
            .ToDictionaryAsync(match => match.Id, match => match.SuppressedAsRepeat);
        Assert.That(decisions, Has.Count.EqualTo(2));
        Assert.That(decisions[selected.Id], Is.False);
        Assert.That(decisions[suppressed.Id], Is.True);
    }
}
