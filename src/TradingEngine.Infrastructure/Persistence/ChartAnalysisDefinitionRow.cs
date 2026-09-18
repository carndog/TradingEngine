namespace TradingEngine.Infrastructure.Persistence;

internal sealed class ChartAnalysisDefinitionRow
{
    public Guid WatchedInstrumentId { get; set; }

    public string DefinitionXml { get; set; } = string.Empty;

    public WatchedInstrumentRow Instrument { get; set; } = null!;
}
