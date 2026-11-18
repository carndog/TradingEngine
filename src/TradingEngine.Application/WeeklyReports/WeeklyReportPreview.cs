using TradingEngine.Application.Signals;

namespace TradingEngine.Application.WeeklyReports;

public sealed record WeeklyReportPreview(DateTimeOffset PeriodStart, DateTimeOffset Cutoff, int TotalCount,
    IReadOnlyList<SignalStatus> Items, int Page, int PageSize, bool HasMore, string Subject, string PlainTextBody, string? HtmlBody);
