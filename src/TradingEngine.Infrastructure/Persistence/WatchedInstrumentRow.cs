using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal sealed class WatchedInstrumentRow
{
    public Guid Id { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Exchange { get; set; } = string.Empty;

    public string QuoteCurrency { get; set; } = string.Empty;

    public string MonitoringState { get; set; } = string.Empty;

    public int SamplingIntervalSeconds { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant LastChangedAt { get; set; }

    public ChartAnalysisDefinitionRow? ChartAnalysisDefinition { get; set; }
}
