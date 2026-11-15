using NodaTime;

namespace TradingEngine.Infrastructure.IntegrationTests.Persistence;

internal sealed class BatchTestClock : IClock
{
    public Instant Now { get; set; } = Instant.FromUtc(2026, 1, 5, 12, 0);

    public Instant GetCurrentInstant() => Now;
}
