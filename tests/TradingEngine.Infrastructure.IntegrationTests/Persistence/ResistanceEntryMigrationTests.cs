using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class ResistanceEntryMigrationTests
{
    private const string PreviousMigration = "20261121000240_RepeatRangeEmailSuppression";
    private const string EntryXml = """
        <ChartAnalysisDefinition priceScale="2"><ResistanceZones>
          <ResistanceZone id="synthetic-zone" lower="95" level="100" upper="105">
            <Condition type="breakout" actionId="publish-signal" />
            <Condition type="resistance-entry" actionId="publish-signal" />
          </ResistanceZone>
        </ResistanceZones></ChartAnalysisDefinition>
        """;

    [Test]
    public async Task MigrateAsync_ExistingEvidence_PreservesRowsAndAcceptsNewCondition()
    {
        await using SignalTestDatabase database = new();
        await database.StartAsync(PreviousMigration);
        NotificationIntentMatchRow original = await database.SeedAsync();
        await using TradingEngineDbContext context = database.CreateContext();
        string originalXml = await context.MonitoringRuleRevisions.Select(row => row.DefinitionXml).SingleAsync();

        await context.Database.MigrateAsync();
        NotificationIntentRow intent = await context.NotificationIntents.SingleAsync();
        NotificationIntentMatchRow entry = SignalTestDatabase.Match(intent);
        entry.Condition = "ResistanceEntry";
        context.Add(entry);
        await context.SaveChangesAsync();

        Assert.That(await context.NotificationIntentMatches.CountAsync(), Is.EqualTo(2));
        Assert.That((await context.NotificationIntentMatches.SingleAsync(row => row.Id == original.Id)).Condition,
            Is.EqualTo(original.Condition));
        Assert.That(await context.MonitoringRuleRevisions.Select(row => row.DefinitionXml).SingleAsync(), Is.EqualTo(originalXml));
    }

    [TestCase("signal")]
    [TestCase("registration")]
    [TestCase("revision")]
    public async Task MigrateAsync_ResistanceEntryInUse_RefusesRollbackWithoutDeletingEvidence(string source)
    {
        await using SignalTestDatabase database = new();
        await database.StartAsync();
        NotificationIntentMatchRow original = await database.SeedAsync();
        await using TradingEngineDbContext context = database.CreateContext();
        NotificationIntentMatchRow match = await context.NotificationIntentMatches.SingleAsync();
        if (source == "signal")
        {
            match.Condition = "ResistanceEntry";
        }
        else if (source == "registration")
        {
            Guid instrumentId = await context.NotificationIntents.Select(row => row.WatchedInstrumentId).SingleAsync();
            context.Add(new ChartAnalysisDefinitionRow { WatchedInstrumentId = instrumentId, DefinitionXml = EntryXml });
        }
        else
        {
            MonitoringRuleRevisionRow revision = await context.MonitoringRuleRevisions.SingleAsync();
            revision.DefinitionXml = EntryXml;
        }

        await context.SaveChangesAsync();
        IMigrator migrator = context.GetService<IMigrator>();

        Assert.That(async () => await migrator.MigrateAsync(PreviousMigration),
            Throws.TypeOf<SqlException>().With.Property(nameof(SqlException.Number)).EqualTo(51000));

        await using TradingEngineDbContext verification = database.CreateContext();
        Assert.That(await verification.NotificationIntentMatches.Select(row => row.Id).SingleAsync(), Is.EqualTo(original.Id));
        Assert.That((await verification.Database.GetAppliedMigrationsAsync()).Last(), Does.EndWith("_ResistanceEntryAlerts"));
        if (source == "signal")
        {
            Assert.That(await verification.NotificationIntentMatches.Select(row => row.Condition).SingleAsync(),
                Is.EqualTo("ResistanceEntry"));
        }
        else if (source == "registration")
        {
            Assert.That(await verification.ChartAnalysisDefinitions.Select(row => row.DefinitionXml).SingleAsync(),
                Does.Contain("resistance-entry"));
        }
        else
        {
            Assert.That(await verification.MonitoringRuleRevisions.Select(row => row.DefinitionXml).SingleAsync(),
                Does.Contain("resistance-entry"));
        }
    }

    [Test]
    public async Task MigrateAsync_EntryUnused_AllowsRollbackAndKeepsExistingSignals()
    {
        await using SignalTestDatabase database = new();
        await database.StartAsync();
        NotificationIntentMatchRow original = await database.SeedAsync();
        await using TradingEngineDbContext context = database.CreateContext();

        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.That(await context.NotificationIntentMatches.Select(row => row.Id).SingleAsync(), Is.EqualTo(original.Id));
        Assert.That((await context.Database.GetAppliedMigrationsAsync()).Last(), Is.EqualTo(PreviousMigration));
    }
}
