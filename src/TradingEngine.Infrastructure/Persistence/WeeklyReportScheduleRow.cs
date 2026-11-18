using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WeeklyReportScheduleRow
{
    public string SubscriptionKey { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public Instant NextCutoff { get; set; }
}
