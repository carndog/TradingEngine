using System.Threading.RateLimiting;

namespace TradingEngine.Api.IntegrationTests.RateLimiting;

internal sealed class ScriptedRateLimitLease : RateLimitLease
{
    private readonly TimeSpan? _retryAfter;

    private ScriptedRateLimitLease(bool acquired, TimeSpan? retryAfter)
    {
        IsAcquired = acquired;
        _retryAfter = retryAfter;
    }

    public override bool IsAcquired { get; }

    public override IEnumerable<string> MetadataNames =>
        _retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

    internal static ScriptedRateLimitLease Acquired()
    {
        return new ScriptedRateLimitLease(true, null);
    }

    internal static ScriptedRateLimitLease Rejected(TimeSpan? retryAfter = null)
    {
        return new ScriptedRateLimitLease(false, retryAfter);
    }

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        if (_retryAfter is not null && metadataName == MetadataName.RetryAfter.Name)
        {
            metadata = _retryAfter;
            return true;
        }

        metadata = null;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
    }
}
