using TradingEngine.Application.Notifications;

namespace TradingEngine.Application.WeeklyReports;

public sealed record WeeklyReportRunResult(bool Generated, bool GenerationFailed, IReadOnlyList<EmailDispatchResult> Deliveries);
