using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace TradingEngine.Api.RateLimiting;

internal sealed class ConcurrencyLimiterFactory : IConcurrencyLimiterFactory
{
    private readonly ApiRateLimitOptions _limits;

    public ConcurrencyLimiterFactory(IOptions<ApiRateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _limits = options.Value;
    }

    public RateLimiter Create()
    {
        return new ConcurrencyLimiter(
            new ConcurrencyLimiterOptions
            {
                PermitLimit = _limits.ConcurrencyPermitLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = _limits.QueueLimit
            });
    }
}
