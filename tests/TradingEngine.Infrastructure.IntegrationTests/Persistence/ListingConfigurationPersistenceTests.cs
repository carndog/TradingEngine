using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Testcontainers.MsSql;
using TradingEngine.Application.Listings;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

[TestFixture]
public sealed class ListingConfigurationPersistenceTests
{
    private const string Database = "ListingConfigurations";
    private static readonly Instant ConfirmedAt = Instant.FromUtc(2026, 1, 2, 9, 30);

    private MsSqlContainer _container = null!;
    private string _connectionString = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        await using TradingEngineDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    [Test]
    public async Task FindAsync_WhenMissing_ReturnsNotFound()
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerListingConfigurationStore store = new(context);

        Result<ListingConfigurationSnapshot> result = await store.FindAsync(
            "XTEST",
            "MISSING",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(InstrumentListingErrors.NotFound));
        });
    }

    [Test]
    public async Task SaveAsync_WhenNew_PersistsRowAndReturnsRowVersion()
    {
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> saved = await store.SaveAsync(
                CreateUpsert("SAVE-1", "GBP"),
                null,
                CancellationToken.None);

            Assert.That(saved.IsSuccess, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(saved.Value.ConcurrencyToken, Is.Not.Empty);
                Assert.That(saved.Value.Listing.Exchange, Is.EqualTo("XTEST"));
                Assert.That(saved.Value.Listing.Symbol, Is.EqualTo("SAVE-1"));
                Assert.That(saved.Value.Listing.Provider, Is.EqualTo("eToro"));
                Assert.That(saved.Value.Listing.ProviderInstrumentId, Is.EqualTo(4242));
                Assert.That(saved.Value.Listing.NativeQuoteCurrency, Is.EqualTo("GBP"));
                Assert.That(
                    saved.Value.Listing.Source,
                    Is.EqualTo(ListingUnitSource.Administrator));
                Assert.That(saved.Value.Listing.ConfirmedBy, Is.EqualTo("test-admin"));
                Assert.That(saved.Value.Listing.ConfirmedAt, Is.EqualTo(ConfirmedAt));
            });
        }

        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT Exchange, Symbol, Provider, ProviderInstrumentId, ProviderSymbol, " +
            "ProviderExchangeName, DisplayName, NativeQuoteCurrency, UnitSource, " +
            "ConfirmedBy, ConfirmedAt, LastChangedAt " +
            "FROM ListingConfigurations WHERE Exchange = 'XTEST' AND Symbol = 'SAVE-1'";
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("XTEST"));
            Assert.That(reader.GetString(1), Is.EqualTo("SAVE-1"));
            Assert.That(reader.GetString(2), Is.EqualTo("eToro"));
            Assert.That(reader.GetInt64(3), Is.EqualTo(4242));
            Assert.That(reader.GetString(4), Is.EqualTo("SAVE-1"));
            Assert.That(reader.GetString(5), Is.EqualTo("Test Exchange"));
            Assert.That(reader.GetString(6), Is.EqualTo("Save One"));
            Assert.That(reader.GetString(7), Is.EqualTo("GBP"));
            Assert.That(reader.GetString(8), Is.EqualTo("Administrator"));
            Assert.That(reader.GetString(9), Is.EqualTo("test-admin"));
            Assert.That(reader.GetDateTime(10), Is.EqualTo(ConfirmedAt.ToDateTimeUtc()));
            Assert.That(reader.GetDateTime(11), Is.EqualTo(ConfirmedAt.ToDateTimeUtc()));
        });
    }

    [Test]
    public async Task FindAsync_AfterSaveInSeparateContext_ReturnsPersistedListing()
    {
        byte[] token;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> saved = await store.SaveAsync(
                CreateUpsert("SEP-1", "GBX"),
                null,
                CancellationToken.None);
            Assert.That(saved.IsSuccess, Is.True);
            token = saved.Value.ConcurrencyToken;
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> found = await store.FindAsync(
                "XTEST",
                "SEP-1",
                CancellationToken.None);

            Assert.That(found.IsSuccess, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(found.Value.Listing.NativeQuoteCurrency, Is.EqualTo("GBX"));
                Assert.That(found.Value.Listing.ProviderInstrumentId, Is.EqualTo(4242));
                Assert.That(found.Value.ConcurrencyToken, Is.EqualTo(token));
            });
        }
    }

    [Test]
    public async Task SaveAsync_WhenUpdatingWithMatchingToken_RefreshesProviderMetadata()
    {
        byte[] token;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> saved = await store.SaveAsync(
                CreateUpsert("UPD-1", "GBP"),
                null,
                CancellationToken.None);
            Assert.That(saved.IsSuccess, Is.True);
            token = saved.Value.ConcurrencyToken;
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            ListingConfigurationUpsert refresh = CreateUpsert("UPD-1", "GBP") with
            {
                ProviderInstrumentId = 7777,
                DisplayName = "Updated One",
                ConfirmedBy = "test-admin-2",
                ConfirmedAt = ConfirmedAt.Plus(Duration.FromMinutes(10))
            };

            Result<ListingConfigurationSnapshot> updated = await store.SaveAsync(
                refresh,
                token,
                CancellationToken.None);

            Assert.That(updated.IsSuccess, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(updated.Value.Listing.ProviderInstrumentId, Is.EqualTo(7777));
                Assert.That(updated.Value.Listing.DisplayName, Is.EqualTo("Updated One"));
                Assert.That(updated.Value.Listing.ConfirmedBy, Is.EqualTo("test-admin-2"));
                Assert.That(
                    updated.Value.Listing.LastChangedAt,
                    Is.EqualTo(ConfirmedAt.Plus(Duration.FromMinutes(10))));
                Assert.That(
                    updated.Value.Listing.ConfirmedAt,
                    Is.EqualTo(ConfirmedAt));
                Assert.That(updated.Value.Listing.NativeQuoteCurrency, Is.EqualTo("GBP"));
                Assert.That(updated.Value.ConcurrencyToken, Is.Not.EqualTo(token));
            });
        }
    }

    [Test]
    public async Task SaveAsync_WhenUpdatingWithoutToken_ReturnsTokenRequired()
    {
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Assert.That(
                (await store.SaveAsync(
                    CreateUpsert("REQ-1", "GBP"),
                    null,
                    CancellationToken.None)).IsSuccess,
                Is.True);
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> result = await store.SaveAsync(
                CreateUpsert("REQ-1", "GBP"),
                null,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFailure, Is.True);
                Assert.That(
                    result.Error,
                    Is.EqualTo(InstrumentListingErrors.ConcurrencyTokenRequired));
            });
        }
    }

    [Test]
    public async Task SaveAsync_WhenUpdatingWithStaleToken_ReturnsConcurrentChange()
    {
        byte[] token;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> saved = await store.SaveAsync(
                CreateUpsert("STALE-1", "GBP"),
                null,
                CancellationToken.None);
            Assert.That(saved.IsSuccess, Is.True);
            token = saved.Value.ConcurrencyToken;
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Assert.That(
                (await store.SaveAsync(
                    CreateUpsert("STALE-1", "GBP") with
                    {
                        ConfirmedAt = ConfirmedAt.Plus(Duration.FromMinutes(1))
                    },
                    token,
                    CancellationToken.None)).IsSuccess,
                Is.True);
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> result = await store.SaveAsync(
                CreateUpsert("STALE-1", "GBP") with
                {
                    ConfirmedAt = ConfirmedAt.Plus(Duration.FromMinutes(2))
                },
                token,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFailure, Is.True);
                Assert.That(
                    result.Error,
                    Is.EqualTo(InstrumentListingErrors.ConcurrentChange));
            });
        }
    }

    [Test]
    public async Task SaveAsync_WhenInsertingWithToken_ReturnsConcurrentChange()
    {
        await using TradingEngineDbContext context = CreateContext();
        SqlServerListingConfigurationStore store = new(context);

        Result<ListingConfigurationSnapshot> result = await store.SaveAsync(
            CreateUpsert("TOKEN-1", "GBP"),
            [1, 2, 3, 4],
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(
                result.Error,
                Is.EqualTo(InstrumentListingErrors.ConcurrentChange));
        });
    }

    [Test]
    public async Task SaveAsync_WhenRelabellingUnit_ReturnsUnitConflict()
    {
        byte[] token;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> saved = await store.SaveAsync(
                CreateUpsert("RELABEL-1", "GBP"),
                null,
                CancellationToken.None);
            Assert.That(saved.IsSuccess, Is.True);
            token = saved.Value.ConcurrencyToken;
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> result = await store.SaveAsync(
                CreateUpsert("RELABEL-1", "USD"),
                token,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFailure, Is.True);
                Assert.That(
                    result.Error.Code,
                    Is.EqualTo(InstrumentListingErrors.UnitConflict.Code));
            });
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Result<ListingConfigurationSnapshot> found = await store.FindAsync(
                "XTEST",
                "RELABEL-1",
                CancellationToken.None);
            Assert.That(found.Value.Listing.NativeQuoteCurrency, Is.EqualTo("GBP"));
        }
    }

    [Test]
    public async Task SaveAsync_WhenUnitSourceViolatesCheckConstraint_Throws()
    {
        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO ListingConfigurations " +
            "(Id, Exchange, Symbol, Provider, ProviderInstrumentId, NativeQuoteCurrency, " +
            "UnitSource, ConfirmedBy, ConfirmedAt, LastChangedAt) " +
            "VALUES (@id, 'XTEST', 'CHK-1', 'eToro', 4242, 'GBP', 'Bogus', 'test-admin', " +
            "'2026-01-02T09:30:00.0000000', '2026-01-02T09:30:00.0000000')";
        command.Parameters.AddWithValue("@id", Guid.NewGuid());

        SqlException? violation = Assert.Throws<SqlException>(
            () => command.ExecuteNonQuery());

        Assert.That(violation!.Number, Is.EqualTo(547));
    }

    [Test]
    public async Task SaveAsync_WhenLastChangedPrecedesConfirmed_ThrowsCheckConstraint()
    {
        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO ListingConfigurations " +
            "(Id, Exchange, Symbol, Provider, ProviderInstrumentId, NativeQuoteCurrency, " +
            "UnitSource, ConfirmedBy, ConfirmedAt, LastChangedAt) " +
            "VALUES (@id, 'XTEST', 'CHK-2', 'eToro', 4242, 'GBP', 'Administrator', " +
            "'test-admin', '2026-01-02T09:30:00.0000000', '2026-01-02T09:00:00.0000000')";
        command.Parameters.AddWithValue("@id", Guid.NewGuid());

        SqlException? violation = Assert.Throws<SqlException>(
            () => command.ExecuteNonQuery());

        Assert.That(violation!.Number, Is.EqualTo(547));
    }

    [Test]
    public async Task SaveAsync_WhenUniqueKeyViolated_Throws()
    {
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore store = new(context);
            Assert.That(
                (await store.SaveAsync(
                    CreateUpsert("UNIQ-1", "GBP"),
                    null,
                    CancellationToken.None)).IsSuccess,
                Is.True);
        }

        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO ListingConfigurations " +
            "(Id, Exchange, Symbol, Provider, ProviderInstrumentId, NativeQuoteCurrency, " +
            "UnitSource, ConfirmedBy, ConfirmedAt, LastChangedAt) " +
            "VALUES (@id, 'XTEST', 'UNIQ-1', 'eToro', 9, 'GBP', 'Administrator', " +
            "'test-admin', '2026-01-02T09:30:00.0000000', '2026-01-02T09:30:00.0000000')";
        command.Parameters.AddWithValue("@id", Guid.NewGuid());

        SqlException? violation = Assert.Throws<SqlException>(
            () => command.ExecuteNonQuery());

        Assert.That(violation!.Number, Is.EqualTo(2601).Or.EqualTo(2627));
    }

    [Test]
    public async Task AddAsync_WhenInstrumentDuplicate_LeavesNewListingRowAbsent()
    {
        ChartAnalysisDefinitionXmlSerializer serializer = new();
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore instruments = new(context, serializer);
            Result<WatchedInstrumentRegistrationReceipt> seeded = await instruments.AddAsync(
                CreateRegistration("ATO-1"),
                CancellationToken.None);
            Assert.That(seeded.IsSuccess, Is.True);
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore instruments = new(context, serializer);
            Result<WatchedInstrumentRegistrationReceipt> duplicate = await instruments.AddAsync(
                CreateRegistration("ATO-1") with
                {
                    Listing = CreateUpsert("ATO-1", "GBP")
                },
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(duplicate.IsFailure, Is.True);
                Assert.That(
                    duplicate.Error,
                    Is.EqualTo(WatchedInstrumentErrors.DuplicateBusinessKey));
            });
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore listings = new(context);
            Result<ListingConfigurationSnapshot> found = await listings.FindAsync(
                "XTEST",
                "ATO-1",
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(found.IsFailure, Is.True);
                Assert.That(found.Error, Is.EqualTo(InstrumentListingErrors.NotFound));
            });
        }
    }

    [Test]
    public async Task AddAsync_WhenInstrumentDuplicate_LeavesExistingListingUnchanged()
    {
        ChartAnalysisDefinitionXmlSerializer serializer = new();
        byte[] token;
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore instruments = new(context, serializer);
            Result<WatchedInstrumentRegistrationReceipt> seeded = await instruments.AddAsync(
                CreateRegistration("ATO-2") with
                {
                    Listing = CreateUpsert("ATO-2", "GBP")
                },
                CancellationToken.None);

            Assert.That(seeded.IsSuccess, Is.True);
            token = seeded.Value.Listing!.ConcurrencyToken;
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore instruments = new(context, serializer);
            Result<WatchedInstrumentRegistrationReceipt> duplicate = await instruments.AddAsync(
                CreateRegistration("ATO-2") with
                {
                    Listing = CreateUpsert("ATO-2", "GBP") with
                    {
                        DisplayName = "Rolled Back",
                        ProviderInstrumentId = 7777
                    },
                    ListingConcurrencyToken = token
                },
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(duplicate.IsFailure, Is.True);
                Assert.That(
                    duplicate.Error,
                    Is.EqualTo(WatchedInstrumentErrors.DuplicateBusinessKey));
            });
        }

        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerListingConfigurationStore listings = new(context);
            Result<ListingConfigurationSnapshot> found = await listings.FindAsync(
                "XTEST",
                "ATO-2",
                CancellationToken.None);

            Assert.That(found.IsSuccess, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(found.Value.ConcurrencyToken, Is.EqualTo(token));
                Assert.That(found.Value.Listing.DisplayName, Is.EqualTo("Save One"));
                Assert.That(found.Value.Listing.ProviderInstrumentId, Is.EqualTo(4242));
                Assert.That(found.Value.Listing.NativeQuoteCurrency, Is.EqualTo("GBP"));
            });
        }
    }

    [Test]
    public async Task AddAsync_WhenListingStageRejected_LeavesNoInstrumentRow()
    {
        ChartAnalysisDefinitionXmlSerializer serializer = new();
        await using (TradingEngineDbContext context = CreateContext())
        {
            SqlServerWatchedInstrumentStore instruments = new(context, serializer);
            Result<WatchedInstrumentRegistrationReceipt> rejected = await instruments.AddAsync(
                CreateRegistration("ATO-3") with
                {
                    Listing = CreateUpsert("ATO-3", "GBP"),
                    ListingConcurrencyToken = [1, 2, 3]
                },
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(rejected.IsFailure, Is.True);
                Assert.That(
                    rejected.Error,
                    Is.EqualTo(InstrumentListingErrors.ConcurrentChange));
            });
        }

        await using SqlConnection connection = new(ConnectionStringFor());
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM WatchedInstruments " +
            "WHERE Exchange = 'XTEST' AND Symbol = 'ATO-3'";
        int rows = (int)(await command.ExecuteScalarAsync())!;

        Assert.That(rows, Is.EqualTo(0));
    }

    private static WatchedInstrumentRegistration CreateRegistration(string symbol)
    {
        WatchedInstrumentFields fields = WatchedInstrument
            .Validate(symbol, "XTEST", "GBP", 60)
            .Value;

        return new WatchedInstrumentRegistration(
            fields,
            MonitoringState.Configured,
            ConfirmedAt,
            ChartAnalysisDefinition.Create(
                4,
                [
                    ChartZone.Create(
                        ChartAnalysisIdentifier.From("support-a").Value,
                        95m,
                        100m,
                        105m,
                        [
                            ChartCondition.Create(
                                ChartConditionType.BuyZone,
                                ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                            ChartCondition.Create(
                                ChartConditionType.SupportLoss,
                                ChartAnalysisIdentifier.From("publish-signal").Value).Value
                        ]).Value
                ],
                []).Value);
    }

    private static ListingConfigurationUpsert CreateUpsert(string symbol, string unit)
    {
        return new ListingConfigurationUpsert(
            "XTEST",
            symbol,
            "eToro",
            4242,
            symbol,
            "Test Exchange",
            "Save One",
            unit,
            ListingUnitSource.Administrator,
            "test-admin",
            ConfirmedAt);
    }

    private TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options =
            new DbContextOptionsBuilder<TradingEngineDbContext>()
                .UseSqlServer(ConnectionStringFor())
                .Options;

        return new TradingEngineDbContext(options);
    }

    private string ConnectionStringFor()
    {
        return $"{_connectionString};Database={Database}";
    }
}
