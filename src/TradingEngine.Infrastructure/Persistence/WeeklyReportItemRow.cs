namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WeeklyReportItemRow
{
    public Guid ReportId { get; set; }

    public int Position { get; set; }

    public Guid SignalId { get; set; }

    public string EvidenceJson { get; set; } = string.Empty;
}
