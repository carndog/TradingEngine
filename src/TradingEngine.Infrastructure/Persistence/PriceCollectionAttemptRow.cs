using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class PriceCollectionAttemptRow
{
    public Guid Id { get; set; }

    public Guid WatchedInstrumentId { get; set; }

    public Instant ScheduledFor { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public string? Provider { get; set; }

    public string? ProviderRequestId { get; set; }

    public Guid InvocationId { get; set; }

    public Instant AttemptedAt { get; set; }

    public Instant RecordedAt { get; set; }

    public WatchedInstrumentRow Instrument { get; set; } = null!;
}
