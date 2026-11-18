using NodaTime;
using TradingEngine.Application.Signals;
using TradingEngine.Domain.Results;

namespace TradingEngine.Application.WeeklyReports;

public sealed class WeeklyReportComposer(ReadSignalsHandler reader, WeeklyReportEmailRenderer renderer, WeeklyReportOptions options)
{
    public async Task<WeeklyReportSnapshot> ComposeAsync(WeeklyReportPeriod period, Guid? reportId,
        IReadOnlyList<string> recipients, int skippedPeriods, CancellationToken cancellationToken)
    {
        List<SignalStatus> items = [];
        await AppendAsync(items, period, false, cancellationToken);
        await AppendAsync(items, period, true, cancellationToken);
        SignalStatus[] ordered = items.OrderBy(item => item.Exchange, StringComparer.Ordinal)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal).ThenBy(item => item.TriggeredAt)
            .ThenBy(item => item.SignalId).ToArray();
        return new WeeklyReportSnapshot(ordered, renderer.Render(period, reportId, ordered, recipients, skippedPeriods));
    }

    private async Task AppendAsync(List<SignalStatus> items, WeeklyReportPeriod period, bool archived, CancellationToken cancellationToken)
    {
        for (int page = 1; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Instant? changedFrom = archived ? period.StartsAt.PlusTicks(1) : null;
            Result<SignalPage> result = await reader.HandleAsync(new SignalQuery(period.Cutoff, Page: page, PageSize: 100,
                Archived: archived, TrackingChangedFrom: changedFrom), cancellationToken);
            if (result.IsFailure)
            {
                throw new InvalidOperationException("The report cutoff or source query is invalid.");
            }

            if (items.Count + result.Value.Items.Count > options.MaxSnapshotRows)
            {
                throw new InvalidOperationException("The report exceeds MaxSnapshotRows; no partial snapshot can be sent.");
            }

            items.AddRange(result.Value.Items);
            if (result.Value.HasMore is false)
            {
                return;
            }
        }
    }
}
