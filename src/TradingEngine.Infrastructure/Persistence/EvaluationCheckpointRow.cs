using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class EvaluationCheckpointRow
{
    public Guid WatchedInstrumentId { get; set; }

    public Instant LastProcessedScheduledFor { get; set; }

    public Instant EstablishedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    public WatchedInstrumentRow Instrument { get; set; } = null!;
}
