namespace TradingEngine.Application.PriceCollection;

public sealed class PriceCollectionOptions
{
    public const string SectionName = "PriceCollection";

    public bool Enabled { get; set; }

    public int PollIntervalSeconds { get; set; } = 15;

    public int MaxExecutionTimeSeconds { get; set; } = 60;

    public int UnavailableRetryAttempts { get; set; } = 1;

    public string SamplingPolicy { get; set; } = SamplingPolicyNames.PerInstrument;

    public int ProviderRateLimit { get; set; } = 120;

    public int ProviderWindowSeconds { get; set; } = 60;

    public Dictionary<string, ExchangeSessionOptions> ExchangeSessions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
