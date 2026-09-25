using Microsoft.EntityFrameworkCore;
using NodaTime;
using TradingEngine.Domain.Instruments;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.Tests.Persistence;

[TestFixture]
public sealed class WatchedInstrumentRowKeyGenerationTests
{
    private static readonly int[] SqlServerSegmentOrder =
        [10, 11, 12, 13, 14, 15, 8, 9, 6, 7, 4, 5, 0, 1, 2, 3];

    [Test]
    public void Add_WhenIdIsUnset_AssignsIdAndPropagatesToDependentKey()
    {
        using TradingEngineDbContext context = CreateContext();
        WatchedInstrumentRow row = CreateRow();

        context.WatchedInstruments.Add(row);

        Assert.Multiple(() =>
        {
            Assert.That(row.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(row.ChartAnalysisDefinition, Is.Not.Null);
            Assert.That(
                row.ChartAnalysisDefinition!.WatchedInstrumentId,
                Is.EqualTo(row.Id));
            Assert.That(
                context.ChangeTracker.Entries<WatchedInstrumentRow>().Single().Entity,
                Is.SameAs(row));
            Assert.That(
                context.ChangeTracker.Entries<ChartAnalysisDefinitionRow>().Single().Entity,
                Is.SameAs(row.ChartAnalysisDefinition));
        });
    }

    [Test]
    public void Add_ForConsecutiveRows_GeneratesUniqueIdsOrderedForSqlServer()
    {
        using TradingEngineDbContext context = CreateContext();
        WatchedInstrumentRow[] rows = Enumerable
            .Range(0, 64)
            .Select(_ => CreateRow())
            .ToArray();

        foreach (WatchedInstrumentRow row in rows)
        {
            context.WatchedInstruments.Add(row);
        }

        Guid[] ids = rows.Select(row => row.Id).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
            Assert.That(ids, Is.Ordered.Ascending.Using<Guid>(CompareForSqlServer));
        });
    }

    private static TradingEngineDbContext CreateContext()
    {
        DbContextOptions<TradingEngineDbContext> options =
            new DbContextOptionsBuilder<TradingEngineDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=TradingEngineKeyGeneration;Encrypt=False")
                .Options;

        return new TradingEngineDbContext(options);
    }

    private static WatchedInstrumentRow CreateRow()
    {
        Instant occurredAt = Instant.FromUtc(2026, 1, 2, 9, 30);
        return new WatchedInstrumentRow
        {
            Symbol = "DEMO-2",
            Exchange = "XTEST",
            QuoteCurrency = "GBP",
            MonitoringState = MonitoringState.Configured.ToString(),
            SamplingIntervalSeconds = 60,
            CreatedAt = occurredAt,
            LastChangedAt = occurredAt,
            ChartAnalysisDefinition = new ChartAnalysisDefinitionRow
            {
                DefinitionXml = "<chart-analysis-definition />"
            }
        };
    }

    private static int CompareForSqlServer(Guid left, Guid right)
    {
        byte[] leftBytes = left.ToByteArray();
        byte[] rightBytes = right.ToByteArray();
        foreach (int index in SqlServerSegmentOrder)
        {
            int comparison = leftBytes[index].CompareTo(rightBytes[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
