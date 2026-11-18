using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class SignalTrackingAuditRow
{
    public long Id { get; set; }

    public Guid SignalId { get; set; }

    public bool Archived { get; set; }

    public Instant ChangedAt { get; set; }

    public string ChangedBy { get; set; } = string.Empty;
}
