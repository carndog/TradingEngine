using TradingEngine.Application.Ports;
using TradingEngine.Application.Signals;

namespace TradingEngine.Application.Tests.WeeklyReports;

internal sealed class WeeklySignalReader : ISignalEvidenceReader
{
    public List<SignalEvidence> Items { get; } = [];
    public List<SignalQuery> Queries { get; } = [];

    public Task<IReadOnlyList<SignalEvidence>> ReadAsync(SignalQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Queries.Add(query);
        IReadOnlyList<SignalEvidence> result = Items.Where(item => item.Archived == query.Archived
            && (query.TrackingChangedFrom is null || item.TrackingChangedAt >= query.TrackingChangedFrom.Value.ToDateTimeOffset()))
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize + 1).ToArray();
        return Task.FromResult(result);
    }
}
