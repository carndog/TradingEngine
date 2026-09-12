namespace TradingEngine.Infrastructure.MonitoringRules.Xml;

public sealed class UnsupportedChartAnalysisSchemaVersionException : Exception
{
    public UnsupportedChartAnalysisSchemaVersionException(string documentNamespace)
        : base($"The chart-analysis schema namespace '{documentNamespace}' is not supported.")
    {
        DocumentNamespace = documentNamespace;
    }

    public string DocumentNamespace { get; }
}
