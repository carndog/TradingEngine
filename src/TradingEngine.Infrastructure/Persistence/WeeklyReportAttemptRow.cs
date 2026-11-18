using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WeeklyReportAttemptRow
{
    public long Id { get; set; }
    public Guid ReportId { get; set; }
    public Instant OccurredAt { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? ProviderOperationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ErrorKind { get; set; }
}
