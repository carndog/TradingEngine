using OpenTelemetry;
using OpenTelemetry.Logs;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.Diagnostics;

internal sealed class TelemetryEnrichmentLogProcessor : BaseProcessor<LogRecord>
{
    private readonly string _version;
    private readonly string _commit;
    private readonly string _environment;

    public TelemetryEnrichmentLogProcessor(VersionResponse identity, string environmentName)
    {
        _version = identity.Version;
        _commit = identity.Commit;
        _environment = environmentName;
    }

    public override void OnEnd(LogRecord logRecord)
    {
        List<KeyValuePair<string, object?>> attributes = logRecord.Attributes is null
            ? new List<KeyValuePair<string, object?>>(3)
            : new List<KeyValuePair<string, object?>>(logRecord.Attributes);

        attributes.Add(new KeyValuePair<string, object?>(
            TelemetryEnrichmentProcessor.VersionTag, _version));
        attributes.Add(new KeyValuePair<string, object?>(
            TelemetryEnrichmentProcessor.CommitTag, _commit));
        attributes.Add(new KeyValuePair<string, object?>(
            TelemetryEnrichmentProcessor.EnvironmentTag, _environment));

        logRecord.Attributes = attributes;
    }
}
