using System.Threading.RateLimiting;

namespace TradingEngine.Api.RateLimiting;

internal interface IRequestBudgetLimiter
{
    PartitionedRateLimiter<HttpContext> Limiter { get; }
}
