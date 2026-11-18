using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WeeklyReportRow
{
    public Guid Id { get; set; }
    public string SubscriptionKey { get; set; } = string.Empty;
    public Instant PeriodStart { get; set; }
    public Instant Cutoff { get; set; }
    public Instant NextCutoff { get; set; }
    public string GenerationStatus { get; set; } = "Generating";
    public string DeliveryStatus { get; set; } = "Skipped";
    public Guid? GenerationClaim { get; set; }
    public Instant? GenerationClaimedUntil { get; set; }
    public string RecipientsJson { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
    public int TotalCount { get; set; }
    public int ArchivedCount { get; set; }
    public int SkippedPeriods { get; set; }
    public Instant? FirstSkippedCutoff { get; set; }
    public string? GenerationError { get; set; }
    public Guid? DeliveryClaim { get; set; }
    public Instant? DeliveryClaimedUntil { get; set; }
    public string? ProviderOperationId { get; set; }
    public int SendAttempts { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
}
