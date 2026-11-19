using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.Tests.Persistence;

[TestFixture]
public sealed class SignalQuoteQueryTests
{
    [TestCase(1)]
    [TestCase(100)]
    public void Latest_BoundedInstrumentPage_TranslatesToCorrelatedTopWithoutRankingHistory(int count)
    {
        using TradingEngineDbContext context = CreateContext();
        Guid[] ids = Enumerable.Range(1, count).Select(number => new Guid(number, 0, 0, new byte[8])).ToArray();

        string sql = SignalQuoteQuery.Latest(context, ids, Instant.FromUtc(2026, 1, 11, 18, 0)).ToQueryString();

        Assert.That(sql, Does.Contain("CROSS APPLY ("));
        Assert.That(sql, Does.Contain("SELECT TOP(1)"));
        Assert.That(sql, Does.Not.Contain("ROW_NUMBER"));
        Assert.That(sql, Does.Match(@"\[WatchedInstrumentId\] = \[\w+\]\.\[Id\]"));
        Assert.That(sql, Does.Not.Contain("[ProviderRequestId]"));
        Assert.That(sql, Does.Not.Contain("[InvocationId]"));
    }

    [Test]
    public void Latest_Cutoff_UsesIndexedObservedTimeAndPreservesAvailabilityBounds()
    {
        using TradingEngineDbContext context = CreateContext();
        Guid[] ids = [new Guid("00000000-0000-0000-0000-000000000001")];

        string sql = SignalQuoteQuery.Latest(context, ids, Instant.FromUtc(2026, 1, 11, 18, 0)).ToQueryString();

        Assert.That(sql, Does.Contain("[EffectiveObservedAt] <= @"));
        Assert.That(sql, Does.Contain("[ScheduledFor] <= @"));
        Assert.That(sql, Does.Contain("[RetrievedAt] <= @"));
        Assert.That(sql, Does.Match(@"ORDER BY \[\w+\]\.\[EffectiveObservedAt\] DESC, \[\w+\]\.\[ScheduledFor\] DESC, \[\w+\]\.\[RetrievedAt\] DESC, \[\w+\]\.\[Id\]"));
        Assert.That(sql, Does.Not.Contain("COALESCE"));
    }

    private static TradingEngineDbContext CreateContext()
    {
        return new TradingEngineDbContext(new DbContextOptionsBuilder<TradingEngineDbContext>()
            .UseSqlServer("Server=localhost;Database=synthetic;Integrated Security=true;TrustServerCertificate=true").Options);
    }
}
