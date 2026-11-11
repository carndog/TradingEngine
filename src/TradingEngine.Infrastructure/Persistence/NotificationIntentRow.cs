using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class NotificationIntentRow
{
    public Guid Id { get; set; }

    public Guid WatchedInstrumentId { get; set; }

    public Instant ScheduledFor { get; set; }

    public Instant? PreviousScheduledFor { get; set; }

    public Guid MonitoringRuleRevisionId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string PriceBasis { get; set; } = string.Empty;

    public string QuoteCurrency { get; set; } = string.Empty;

    public decimal? CurrentBid { get; set; }

    public decimal? CurrentAsk { get; set; }

    public Guid? CollectionInvocationId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ProviderOperationId { get; set; }

    public Guid? ClaimToken { get; set; }

    public Instant? ClaimedUntil { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    public WatchedInstrumentRow Instrument { get; set; } = null!;

    public ICollection<NotificationIntentMatchRow> Matches { get; set; } = [];
}
