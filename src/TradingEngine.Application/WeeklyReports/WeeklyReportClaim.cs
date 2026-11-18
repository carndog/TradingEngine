namespace TradingEngine.Application.WeeklyReports;

public sealed record WeeklyReportClaim(
    Guid ReportId,
    string SubscriptionKey,
    WeeklyReportPeriod Period,
    Guid ClaimToken,
    IReadOnlyList<string> Recipients,
    int SkippedPeriods,
    DateTimeOffset? FirstSkippedCutoff);
