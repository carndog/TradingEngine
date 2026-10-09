using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TradingEngine.Api.IntegrationTests.RateLimiting;

internal sealed class CountingProbeHealthCheck : IHealthCheck
{
    private int _invocationCount;

    public int InvocationCount => _invocationCount;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocationCount);

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
