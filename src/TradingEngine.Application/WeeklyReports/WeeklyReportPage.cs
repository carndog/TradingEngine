using TradingEngine.Application.Signals;

namespace TradingEngine.Application.WeeklyReports;

public sealed record WeeklyReportPage(
    Guid ReportId,
    DateTimeOffset PeriodStart,
    DateTimeOffset Cutoff,
    string GenerationStatus,
    string DeliveryStatus,
    int TotalCount,
    int ArchivedCount,
    int SkippedPeriods,
    DateTimeOffset? FirstSkippedCutoff,
    string? GenerationError,
    IReadOnlyList<SignalStatus> Items,
    int Page,
    int PageSize,
    bool HasMore,
    string Subject,
    string PlainTextBody,
    string? HtmlBody);
