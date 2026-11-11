namespace TradingEngine.Application.Recommendations;

public sealed class RecommendationOptions
{
    public const string SectionName = "Recommendations";

    public bool Enabled { get; set; }

    public int PollIntervalSeconds { get; set; } = 30;

    public int MaxObservationsPerInstrument { get; set; } = 200;

    public int MaxExecutionTimeSeconds { get; set; } = 30;

    public string PriceBasis { get; set; } = "Ask";
}
