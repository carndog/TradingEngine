using System.Threading.RateLimiting;

namespace TradingEngine.Api.RateLimiting;

internal interface IConcurrencyLimiterFactory
{
    RateLimiter Create();
}
