using TradingEngine.Application.Ports;

namespace TradingEngine.Application.PriceCollection;

public sealed class ProviderBudgetClampedSamplingPolicy : ISamplingPolicy
{
    private readonly int _providerRateLimit;
    private readonly int _providerWindowSeconds;

    public ProviderBudgetClampedSamplingPolicy(
        int providerRateLimit,
        int providerWindowSeconds)
    {
        if (providerRateLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerRateLimit),
                providerRateLimit,
                "The provider rate limit must be at least 1 request.");
        }

        if (providerWindowSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerWindowSeconds),
                providerWindowSeconds,
                "The provider rate-limit window must be at least 1 second.");
        }

        _providerRateLimit = providerRateLimit;
        _providerWindowSeconds = providerWindowSeconds;
    }

    public string Name => SamplingPolicyNames.ProviderBudgetClamped;

    public int EffectiveIntervalSeconds(
        MonitoredInstrument instrument,
        int monitoredCount)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        int minimum = MinimumIntervalSeconds(Math.Max(1, monitoredCount));
        return Math.Max(instrument.SamplingIntervalSeconds, minimum);
    }

    private int MinimumIntervalSeconds(int instrumentCount)
    {
        double requestsPerSecond = (double)_providerRateLimit / _providerWindowSeconds;
        return (int)Math.Ceiling(instrumentCount / requestsPerSecond);
    }
}
