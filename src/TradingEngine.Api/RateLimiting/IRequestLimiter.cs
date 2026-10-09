using System.Threading.RateLimiting;

namespace TradingEngine.Api.RateLimiting;

internal interface IRequestLimiter
{
    ValueTask<RateLimitLease> AcquireAsync(HttpContext context, CancellationToken cancellationToken);
}
