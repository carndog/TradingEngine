using System.ComponentModel.DataAnnotations;

namespace TradingEngine.Api.RateLimiting;

public sealed class ApiRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    [Range(1, 100000)]
    public int ReadPermitLimit { get; set; } = 120;

    [Range(1, 100000)]
    public int WritePermitLimit { get; set; } = 30;

    [Range(1, 1024)]
    public int ConcurrencyPermitLimit { get; set; } = 8;

    [Range(0, 16)]
    public int QueueLimit { get; set; }

    [Range(1, 100000)]
    public int DatabaseProbePermitLimit { get; set; } = 10;
}
