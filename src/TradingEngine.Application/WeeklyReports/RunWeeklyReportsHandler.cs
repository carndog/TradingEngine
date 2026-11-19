using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;

namespace TradingEngine.Application.WeeklyReports;

public sealed class RunWeeklyReportsHandler(IWeeklyReportStore store, WeeklyReportSchedule schedule,
    WeeklyReportComposer composer, WeeklyReportOptions options, IClock clock, IEmailSender? sender = null)
{
    public async Task<WeeklyReportRunResult> HandleAsync(CancellationToken cancellationToken)
    {
        Instant now = clock.GetCurrentInstant();
        Instant? next = await store.ConfigureScheduleAsync(options.SubscriptionKey, options.Enabled, schedule.Fingerprint,
            schedule.NextAfter(now), now, cancellationToken);
        if (options.Enabled is false || next is null)
        {
            return new WeeklyReportRunResult(false, false, []);
        }

        if (sender is null)
        {
            throw new InvalidOperationException("Enabled weekly reporting requires an email sender.");
        }

        Instant cutoff = schedule.LatestOnOrBefore(now);
        WeeklyReportClaim? claim = await store.TryClaimGenerationAsync(new WeeklyReportGenerationRequest(
            options.SubscriptionKey, next.Value, schedule.PeriodEndingAt(cutoff), schedule.SkippedPeriods(next.Value, cutoff),
            options.RecipientAddresses.ToArray(), Guid.CreateVersion7(), now.Plus(Duration.FromSeconds(options.GenerationLeaseSeconds)), now), cancellationToken);
        bool completed = false;
        bool generationFailed = false;
        if (claim is not null)
        {
            try
            {
                completed = await store.CompleteGenerationAsync(claim,
                    token => composer.ComposeAsync(claim.Period, claim.ReportId, claim.Recipients, claim.SkippedPeriods, token),
                    clock.GetCurrentInstant(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                generationFailed = true;
                await store.RecordGenerationFailureAsync(claim.ReportId, claim.ClaimToken, cancellationToken);
            }
        }

        IReadOnlyList<EmailDeliveryWork> pending = await store.ListDispatchableAsync(options.SubscriptionKey,
            options.MaxReportsPerRun, clock.GetCurrentInstant(), cancellationToken);
        EmailDeliveryDispatcher dispatcher = new(store, sender, clock, options.ClaimLeaseSeconds, options.MaxDispatchAttempts);
        List<EmailDispatchResult> outcomes = [];
        foreach (EmailDeliveryWork report in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(await dispatcher.DispatchAsync(report, cancellationToken));
        }

        return new WeeklyReportRunResult(completed, generationFailed, outcomes);
    }
}
