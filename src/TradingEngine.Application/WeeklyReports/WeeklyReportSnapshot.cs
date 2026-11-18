using TradingEngine.Application.Notifications;
using TradingEngine.Application.Signals;

namespace TradingEngine.Application.WeeklyReports;

public sealed record WeeklyReportSnapshot(IReadOnlyList<SignalStatus> Items, RecommendationEmail Email);
