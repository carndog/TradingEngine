using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure.Tests.Persistence;

[TestFixture]
public sealed class SequentialWatchedInstrumentIdGeneratorTests
{
    private static readonly int[] SqlServerSegmentOrder =
        [10, 11, 12, 13, 14, 15, 8, 9, 6, 7, 4, 5, 0, 1, 2, 3];

    [Test]
    public void NewId_Always_GeneratesUniqueIdsOrderedForSqlServer()
    {
        SequentialWatchedInstrumentIdGenerator generator = new();

        Guid[] ids = Enumerable
            .Range(0, 1024)
            .Select(_ => generator.NewId())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
            Assert.That(ids, Is.Ordered.Ascending.Using<Guid>(CompareForSqlServer));
        });
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
