using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class NotificationAttemptRow
{
    public Guid Id { get; set; }

    public Guid NotificationIntentId { get; set; }

    public Instant AttemptedAt { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public string? ProviderOperationId { get; set; }

    public string? ProviderStatus { get; set; }

    public string? ErrorKind { get; set; }

    public Instant RecordedAt { get; set; }

    public NotificationIntentRow Intent { get; set; } = null!;
}
