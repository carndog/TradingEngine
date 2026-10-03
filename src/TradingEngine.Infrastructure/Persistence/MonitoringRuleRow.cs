using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class MonitoringRuleRow
{
    public Guid Id { get; set; }

    public Guid WatchedInstrumentId { get; set; }

    public Instant CreatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public WatchedInstrumentRow Instrument { get; set; } = null!;

    public ICollection<MonitoringRuleRevisionRow> Revisions { get; set; } = [];
}
