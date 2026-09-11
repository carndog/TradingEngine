using NodaTime;

namespace TradingEngine.Domain.Instruments;

public sealed class WatchedInstrument
{
    private WatchedInstrument(
        WatchedInstrumentId id,
        InstrumentSymbol symbol,
        ExchangeCode exchange,
        QuoteCurrencyCode quoteCurrency,
        SamplingPolicy samplingPolicy,
        Instant createdAt)
    {
        Id = id;
        Symbol = symbol;
        Exchange = exchange;
        QuoteCurrency = quoteCurrency;
        SamplingPolicy = samplingPolicy;
        MonitoringState = MonitoringState.Configured;
        CreatedAt = createdAt;
        LastChangedAt = createdAt;
    }

    public WatchedInstrumentId Id { get; }

    public InstrumentSymbol Symbol { get; }

    public ExchangeCode Exchange { get; }

    public QuoteCurrencyCode QuoteCurrency { get; }

    public MonitoringState MonitoringState { get; private set; }

    public SamplingPolicy SamplingPolicy { get; private set; }

    public Instant CreatedAt { get; }

    public Instant LastChangedAt { get; private set; }

    public static WatchedInstrument Create(
        WatchedInstrumentId id,
        InstrumentSymbol symbol,
        ExchangeCode exchange,
        QuoteCurrencyCode quoteCurrency,
        SamplingPolicy samplingPolicy,
        Instant createdAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(quoteCurrency);
        EnsureValidSamplingPolicy(samplingPolicy);

        return new WatchedInstrument(
            id,
            symbol,
            exchange,
            quoteCurrency,
            samplingPolicy,
            createdAt);
    }

    public void StartMonitoring(SamplingPolicy samplingPolicy, Instant changedAt)
    {
        EnsureChangeTimestamp(changedAt);
        EnsureValidSamplingPolicy(samplingPolicy);

        if (MonitoringState == MonitoringState.Monitored)
        {
            throw new DomainRuleViolationException("The instrument is already being monitored.");
        }

        SamplingPolicy = samplingPolicy;
        MonitoringState = MonitoringState.Monitored;
        LastChangedAt = changedAt;
    }

    public void StopMonitoring(Instant changedAt)
    {
        EnsureChangeTimestamp(changedAt);

        if (MonitoringState == MonitoringState.Configured)
        {
            throw new DomainRuleViolationException("The instrument is not currently being monitored.");
        }

        MonitoringState = MonitoringState.Configured;
        LastChangedAt = changedAt;
    }

    public void ChangeSamplingPolicy(SamplingPolicy samplingPolicy, Instant changedAt)
    {
        EnsureChangeTimestamp(changedAt);
        EnsureValidSamplingPolicy(samplingPolicy);

        if (SamplingPolicy == samplingPolicy)
        {
            throw new DomainRuleViolationException("The requested sampling policy is already assigned.");
        }

        SamplingPolicy = samplingPolicy;
        LastChangedAt = changedAt;
    }

    private static void EnsureValidSamplingPolicy(SamplingPolicy samplingPolicy)
    {
        if (!Enum.IsDefined(samplingPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(samplingPolicy), samplingPolicy, "Unknown sampling policy.");
        }
    }

    private void EnsureChangeTimestamp(Instant changedAt)
    {
        if (changedAt < LastChangedAt)
        {
            throw new DomainRuleViolationException(
                "A change cannot be recorded before the instrument's latest change.");
        }
    }
}
