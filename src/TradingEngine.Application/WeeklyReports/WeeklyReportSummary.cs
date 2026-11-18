namespace TradingEngine.Application.WeeklyReports;

public sealed record WeeklyReportSummary(Guid ReportId, DateTimeOffset Cutoff, string GenerationStatus,
    string DeliveryStatus, int TotalCount, int SkippedPeriods, string? GenerationError);
