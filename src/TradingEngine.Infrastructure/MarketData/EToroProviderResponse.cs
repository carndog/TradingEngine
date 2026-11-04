using System.Net;

namespace TradingEngine.Infrastructure.MarketData;

internal sealed class EToroProviderResponse
{
    public required HttpStatusCode StatusCode { get; init; }

    public required string RequestId { get; init; }

    public string? Body { get; init; }

    public bool ClientThrottled { get; init; }

    public TimeSpan? RetryAfter { get; init; }

    public int? RateLimitRemaining { get; init; }

    public int? RateLimitResetSeconds { get; init; }
}
