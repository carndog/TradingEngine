namespace TradingEngine.Client.Services;

public sealed class InstrumentListState
{
    public string ListUrl { get; set; } = "instruments";

    public Guid? SelectedId { get; set; }
}
