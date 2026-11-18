using NodaTime;

namespace TradingEngine.Application.WeeklyReports;

public sealed record WeeklyReportPeriod(Instant StartsAt, Instant Cutoff, Instant NextCutoff);
