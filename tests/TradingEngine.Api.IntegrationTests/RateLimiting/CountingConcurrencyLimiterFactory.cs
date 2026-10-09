using System.Threading.RateLimiting;
using TradingEngine.Api.RateLimiting;

namespace TradingEngine.Api.IntegrationTests.RateLimiting;

internal sealed class CountingConcurrencyLimiterFactory : IConcurrencyLimiterFactory
{
    private readonly ConcurrencyLimiterOptions _options;
    private readonly List<ConcurrencyLimiter> _created = [];

    public CountingConcurrencyLimiterFactory(int permitLimit, int queueLimit = 0)
    {
        _options = new ConcurrencyLimiterOptions
        {
            PermitLimit = permitLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = queueLimit
        };
    }

    public int CreatedCount => _created.Count;

    public ConcurrencyLimiter Active => _created[^1];

    public RateLimiter Create()
    {
        ConcurrencyLimiter limiter = new(_options);
        _created.Add(limiter);

        return limiter;
    }
}
