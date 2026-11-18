using NodaTime;

namespace TradingEngine.Application.WeeklyReports;

public sealed record WeeklyReportGenerationRequest(
    string SubscriptionKey,
    Instant ExpectedNextCutoff,
    WeeklyReportPeriod Period,
    int SkippedPeriods,
    IReadOnlyList<string> Recipients,
    Guid ClaimToken,
    Instant ClaimedUntil,
    Instant Now);
