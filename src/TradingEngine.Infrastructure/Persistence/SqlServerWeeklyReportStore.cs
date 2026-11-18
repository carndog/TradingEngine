using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Signals;
using TradingEngine.Application.Time;
using TradingEngine.Application.WeeklyReports;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class SqlServerWeeklyReportStore(TradingEngineDbContext context) : IWeeklyReportStore
{
    public async Task<Instant?> ConfigureScheduleAsync(string key, bool enabled, string fingerprint,
        Instant firstCutoff, Instant now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        PersistedInstant.Require(firstCutoff, nameof(firstCutoff));
        PersistedInstant.Require(now, nameof(now));
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        WeeklyReportScheduleRow? schedule = await LockScheduleAsync(key, cancellationToken);
        if (schedule is null)
        {
            schedule = new WeeklyReportScheduleRow { SubscriptionKey = key, Enabled = enabled, Fingerprint = fingerprint, NextCutoff = firstCutoff };
            context.WeeklyReportSchedules.Add(schedule);
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            Instant next = enabled && (schedule.Enabled is false || schedule.Fingerprint != fingerprint)
                ? firstCutoff : schedule.NextCutoff;
            await context.WeeklyReportSchedules.Where(row => row.SubscriptionKey == key)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Enabled, enabled)
                    .SetProperty(row => row.Fingerprint, fingerprint).SetProperty(row => row.NextCutoff, next), cancellationToken);
            schedule.NextCutoff = next;
        }

        await transaction.CommitAsync(cancellationToken);
        return enabled ? schedule.NextCutoff : null;
    }

    public async Task<WeeklyReportClaim?> TryClaimGenerationAsync(WeeklyReportGenerationRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        WeeklyReportScheduleRow? schedule = await LockScheduleAsync(request.SubscriptionKey, cancellationToken);
        if (schedule is null || schedule.Enabled is false)
        {
            return null;
        }

        WeeklyReportRow? report = await context.WeeklyReports.AsNoTracking()
            .Where(row => row.SubscriptionKey == request.SubscriptionKey && row.GenerationStatus == "Generating")
            .OrderBy(row => row.Cutoff).FirstOrDefaultAsync(cancellationToken);
        if (report is not null)
        {
            if (report.GenerationClaimedUntil > request.Now)
            {
                return null;
            }

            await context.WeeklyReports.Where(row => row.Id == report.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.GenerationClaim, request.ClaimToken)
                    .SetProperty(row => row.GenerationClaimedUntil, request.ClaimedUntil), cancellationToken);
            report.GenerationClaim = request.ClaimToken;
        }
        else
        {
            if (schedule.NextCutoff != request.ExpectedNextCutoff || schedule.NextCutoff > request.Now
                || request.Period.Cutoff < schedule.NextCutoff)
            {
                return null;
            }

            report = new WeeklyReportRow
            {
                Id = Guid.CreateVersion7(), SubscriptionKey = request.SubscriptionKey,
                PeriodStart = request.Period.StartsAt, Cutoff = request.Period.Cutoff, NextCutoff = request.Period.NextCutoff,
                GenerationClaim = request.ClaimToken, GenerationClaimedUntil = request.ClaimedUntil,
                RecipientsJson = JsonSerializer.Serialize(request.Recipients), SkippedPeriods = request.SkippedPeriods,
                FirstSkippedCutoff = request.SkippedPeriods > 0 ? schedule.NextCutoff : null,
                CreatedAt = request.Now, UpdatedAt = request.Now
            };
            context.WeeklyReports.Add(report);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Claim(report);
    }

    public async Task<bool> CompleteGenerationAsync(WeeklyReportClaim claim,
        Func<CancellationToken, Task<WeeklyReportSnapshot>> compose, Instant now, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockScheduleAsync(claim.SubscriptionKey, cancellationToken);
        WeeklyReportRow? report = await LockReportAsync(claim.ReportId, cancellationToken);
        if (report is null || report.GenerationStatus != "Generating" || report.GenerationClaim != claim.ClaimToken
            || report.GenerationClaimedUntil <= now)
        {
            return false;
        }

        try
        {
            WeeklyReportSnapshot snapshot = await compose(cancellationToken);
            if (snapshot.Items.Count > 100000 || snapshot.Items.Select(item => item.SignalId).Distinct().Count() != snapshot.Items.Count
                || snapshot.Email.RecipientAddresses is null
                || snapshot.Email.RecipientAddresses.SequenceEqual(Recipients(report)) is false)
            {
                throw new InvalidDataException("The report snapshot has invalid membership or recipients.");
            }

            for (int position = 0; position < snapshot.Items.Count; position++)
            {
                SignalStatus item = snapshot.Items[position];
                context.WeeklyReportItems.Add(new WeeklyReportItemRow
                {
                    ReportId = report.Id, Position = position, SignalId = item.SignalId,
                    EvidenceJson = JsonSerializer.Serialize(item)
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            string generationStatus = snapshot.Items.Count == 0 ? "Empty" : "Ready";
            string deliveryStatus = snapshot.Items.Count == 0 ? "Skipped" : "Pending";
            int archivedCount = snapshot.Items.Count(item => item.Archived);
            await context.WeeklyReports.Where(row => row.Id == report.Id && row.GenerationClaim == claim.ClaimToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.GenerationStatus, generationStatus).SetProperty(row => row.DeliveryStatus, deliveryStatus)
                    .SetProperty(row => row.Subject, snapshot.Email.Subject).SetProperty(row => row.PlainTextBody, snapshot.Email.PlainTextBody)
                    .SetProperty(row => row.HtmlBody, snapshot.Email.HtmlBody).SetProperty(row => row.TotalCount, snapshot.Items.Count)
                    .SetProperty(row => row.ArchivedCount, archivedCount).SetProperty(row => row.GenerationError, (string?)null)
                    .SetProperty(row => row.GenerationClaim, (Guid?)null).SetProperty(row => row.GenerationClaimedUntil, (Instant?)null)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            await context.WeeklyReportSchedules.Where(row => row.SubscriptionKey == report.SubscriptionKey)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NextCutoff, report.NextCutoff), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task RecordGenerationFailureAsync(Guid reportId, Guid claimToken, CancellationToken cancellationToken)
    {
        await context.WeeklyReports.Where(row => row.Id == reportId && row.GenerationClaim == claimToken && row.GenerationStatus == "Generating")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.GenerationError, "generation_failed"), cancellationToken);
    }

    public async Task<IReadOnlyList<EmailDeliveryWork>> ListDispatchableAsync(string key, int maximum, Instant now, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 100);
        List<WeeklyReportRow> rows = await Dispatchable(now).Where(row => row.SubscriptionKey == key)
            .OrderBy(row => row.UpdatedAt).ThenBy(row => row.Id).Take(maximum).ToListAsync(cancellationToken);
        return rows.Select(row => new EmailDeliveryWork(row.Id, () =>
            new RecommendationEmail(row.Subject, row.PlainTextBody, row.HtmlBody, Recipients(row)))).ToArray();
    }

    public async Task<NotificationClaim?> TryClaimAsync(Guid deliveryId, Guid claimToken, Instant claimedUntil,
        Instant now, CancellationToken cancellationToken)
    {
        int updated = await Dispatchable(now).Where(row => row.Id == deliveryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DeliveryClaim, claimToken)
                .SetProperty(row => row.DeliveryClaimedUntil, claimedUntil).SetProperty(row => row.UpdatedAt, now), cancellationToken);
        if (updated == 0)
        {
            return null;
        }

        WeeklyReportRow? row = await context.WeeklyReports.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == deliveryId && item.DeliveryClaim == claimToken, cancellationToken);
        return row is null ? null : new NotificationClaim(Enum.Parse<NotificationStatus>(row.DeliveryStatus), row.ProviderOperationId);
    }

    public async Task<BeginSubmissionResult> TryBeginSubmissionAsync(Guid deliveryId, Guid claimToken,
        string providerOperationId, int maxAttempts, Instant now, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        WeeklyReportRow? row = await LockReportAsync(deliveryId, cancellationToken);
        if (row is null || row.DeliveryClaim != claimToken || row.DeliveryClaimedUntil <= now
            || row.DeliveryStatus != "Pending" || row.ProviderOperationId is not null)
        {
            return BeginSubmissionResult.ClaimLost;
        }

        if (row.SendAttempts >= maxAttempts)
        {
            return BeginSubmissionResult.AttemptsExhausted;
        }

        await context.WeeklyReports.Where(item => item.Id == deliveryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ProviderOperationId, providerOperationId)
                .SetProperty(item => item.SendAttempts, item => item.SendAttempts + 1), cancellationToken);
        context.WeeklyReportAttempts.Add(new WeeklyReportAttemptRow
        {
            ReportId = deliveryId, OccurredAt = now, Outcome = "SubmissionStarted", ProviderOperationId = providerOperationId
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BeginSubmissionResult.Began;
    }

    public async Task<bool> RecordOutcomeAsync(EmailDeliveryOutcome outcome, CancellationToken cancellationToken)
    {
        PersistedInstant.Require(outcome.RecordedAt, nameof(outcome));
        PersistedInstant.Require(outcome.AttemptedAt, nameof(outcome));
        if (Enum.IsDefined(outcome.Status) is false || Enum.IsDefined(outcome.AttemptOutcome) is false)
        {
            throw new InvalidDataException("Undefined email delivery outcome.");
        }

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        string status = outcome.Status.ToString();
        string attempt = outcome.AttemptOutcome.ToString();
        DateTime at = outcome.RecordedAt.ToDateTimeUtc();
        int changed = await context.Database.ExecuteSqlAsync($"""
            UPDATE WeeklyReports SET DeliveryStatus = {status},
                ProviderOperationId = CASE WHEN {attempt} = 'Failed' THEN NULL ELSE COALESCE({outcome.ProviderOperationId}, ProviderOperationId) END,
                DeliveryClaim = NULL, DeliveryClaimedUntil = NULL, UpdatedAt = {at}
            WHERE Id = {outcome.DeliveryId} AND DeliveryClaim = {outcome.ClaimToken} AND DeliveryClaimedUntil > {at}
            """, cancellationToken);
        if (changed == 0)
        {
            return false;
        }

        context.WeeklyReportAttempts.Add(new WeeklyReportAttemptRow
        {
            ReportId = outcome.DeliveryId, OccurredAt = outcome.AttemptedAt, Outcome = attempt,
            ProviderOperationId = outcome.ProviderOperationId, ProviderStatus = outcome.ProviderStatus, ErrorKind = outcome.ErrorKind
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<WeeklyReportPage?> ReadAsync(string key, Guid reportId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        WeeklyReportRow? report = await context.WeeklyReports.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SubscriptionKey == key && row.Id == reportId, cancellationToken);
        if (report is null)
        {
            return null;
        }

        List<string> evidence = await context.WeeklyReportItems.AsNoTracking().Where(row => row.ReportId == reportId)
            .OrderBy(row => row.Position).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => row.EvidenceJson).ToListAsync(cancellationToken);
        SignalStatus[] items = evidence.Select(json => JsonSerializer.Deserialize<SignalStatus>(json)
            ?? throw new InvalidDataException("Stored signal evidence is invalid.")).ToArray();
        return new WeeklyReportPage(report.Id, report.PeriodStart.ToDateTimeOffset(), report.Cutoff.ToDateTimeOffset(),
            report.GenerationStatus, report.DeliveryStatus, report.TotalCount, report.ArchivedCount, report.SkippedPeriods,
            report.FirstSkippedCutoff?.ToDateTimeOffset(), report.GenerationError, items, page, pageSize,
            (long)page * pageSize < report.TotalCount, report.Subject, report.PlainTextBody, report.HtmlBody);
    }

    private IQueryable<WeeklyReportRow> Dispatchable(Instant now)
    {
        return context.WeeklyReports.AsNoTracking().Where(row => row.GenerationStatus == "Ready"
            && (row.DeliveryStatus == "Pending" || row.DeliveryStatus == "Submitted" || row.DeliveryStatus == "Ambiguous")
            && (row.DeliveryClaimedUntil == null || row.DeliveryClaimedUntil <= now)
            && context.WeeklyReportSchedules.Any(schedule => schedule.SubscriptionKey == row.SubscriptionKey && schedule.Enabled));
    }

    public async Task<IReadOnlyList<WeeklyReportSummary>> ListAsync(string key, int maximum, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 100);
        return await context.WeeklyReports.AsNoTracking().Where(row => row.SubscriptionKey == key)
            .OrderByDescending(row => row.Cutoff).Take(maximum)
            .Select(row => new WeeklyReportSummary(row.Id, row.Cutoff.ToDateTimeOffset(), row.GenerationStatus,
                row.DeliveryStatus, row.TotalCount, row.SkippedPeriods, row.GenerationError))
            .ToListAsync(cancellationToken);
    }

    private Task<WeeklyReportScheduleRow?> LockScheduleAsync(string key, CancellationToken cancellationToken)
    {
        return context.WeeklyReportSchedules
            .FromSqlInterpolated($"SELECT * FROM WeeklyReportSchedules WITH (UPDLOCK, HOLDLOCK) WHERE SubscriptionKey = {key}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    private Task<WeeklyReportRow?> LockReportAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.WeeklyReports.FromSqlInterpolated($"SELECT * FROM WeeklyReports WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    private static string[] Recipients(WeeklyReportRow report)
    {
        return JsonSerializer.Deserialize<string[]>(report.RecipientsJson)
            ?? throw new InvalidDataException("The report recipient snapshot is missing.");
    }

    private static WeeklyReportClaim Claim(WeeklyReportRow report)
    {
        return new WeeklyReportClaim(report.Id, report.SubscriptionKey,
            new WeeklyReportPeriod(report.PeriodStart, report.Cutoff, report.NextCutoff), report.GenerationClaim!.Value,
            Recipients(report), report.SkippedPeriods, report.FirstSkippedCutoff?.ToDateTimeOffset());
    }

    private static void Validate(WeeklyReportGenerationRequest request)
    {
        PersistedInstant.Require(request.Period.StartsAt, nameof(request));
        PersistedInstant.Require(request.Period.Cutoff, nameof(request));
        PersistedInstant.Require(request.Period.NextCutoff, nameof(request));
        PersistedInstant.Require(request.ClaimedUntil, nameof(request));
        PersistedInstant.Require(request.Now, nameof(request));
        if (request.Period.StartsAt >= request.Period.Cutoff || request.Period.Cutoff >= request.Period.NextCutoff
            || request.ClaimedUntil <= request.Now || request.Period.Cutoff > request.Now || request.SkippedPeriods < 0
            || request.Recipients.Count == 0 || request.Recipients.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The weekly generation request is invalid.", nameof(request));
        }
    }
}
