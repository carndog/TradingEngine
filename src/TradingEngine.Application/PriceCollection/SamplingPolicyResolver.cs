using TradingEngine.Application.Ports;

namespace TradingEngine.Application.PriceCollection;

public static class SamplingPolicyResolver
{
    public static ISamplingPolicy Resolve(
        string? name,
        int providerRateLimit,
        int providerWindowSeconds)
    {
        string resolved = string.IsNullOrWhiteSpace(name)
            ? SamplingPolicyNames.PerInstrument
            : name.Trim();

        if (string.Equals(
                resolved, SamplingPolicyNames.PerInstrument, StringComparison.OrdinalIgnoreCase))
        {
            return new PerInstrumentSamplingPolicy();
        }

        if (string.Equals(
                resolved,
                SamplingPolicyNames.ProviderBudgetClamped,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderBudgetClampedSamplingPolicy(
                providerRateLimit,
                providerWindowSeconds);
        }

        throw new InvalidOperationException(
            $"The price-collection sampling policy '{resolved}' is not supported. " +
            $"Supported policies are '{SamplingPolicyNames.PerInstrument}' and " +
            $"'{SamplingPolicyNames.ProviderBudgetClamped}'.");
    }
}
