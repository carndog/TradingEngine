using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class PriceObservationRow
{
    public Guid Id { get; set; }

    public Guid WatchedInstrumentId { get; set; }

    public Instant ScheduledFor { get; set; }

    public string Provider { get; set; } = string.Empty;

    public long ProviderInstrumentId { get; set; }

    public decimal? Bid { get; set; }

    public decimal? Ask { get; set; }

    public string QuoteCurrency { get; set; } = string.Empty;

    public Instant? ProviderObservedAt { get; set; }

    public Instant RetrievedAt { get; set; }

    public string Timeliness { get; set; } = string.Empty;

    public string? ProviderRequestId { get; set; }

    public Guid InvocationId { get; set; }

    public WatchedInstrumentRow Instrument { get; set; } = null!;
}
