using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.WeeklyReports;

namespace TradingEngine.Application.Ports;

public interface IWeeklyReportStore : IEmailDeliveryStore
{
    Task<Instant?> ConfigureScheduleAsync(string key, bool enabled, string fingerprint,
        Instant firstCutoff, Instant now, CancellationToken cancellationToken);

    Task<WeeklyReportClaim?> TryClaimGenerationAsync(WeeklyReportGenerationRequest request, CancellationToken cancellationToken);

    Task<bool> CompleteGenerationAsync(WeeklyReportClaim claim, Func<CancellationToken, Task<WeeklyReportSnapshot>> compose,
        Instant now, CancellationToken cancellationToken);

    Task RecordGenerationFailureAsync(Guid reportId, Guid claimToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmailDeliveryWork>> ListDispatchableAsync(string key, int maximum, Instant now, CancellationToken cancellationToken);

    Task<WeeklyReportPage?> ReadAsync(string key, Guid reportId, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<WeeklyReportSummary>> ListAsync(string key, int maximum, CancellationToken cancellationToken);
}
