using System.Diagnostics;
using OpenTelemetry;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.Diagnostics;

internal sealed class TelemetryEnrichmentProcessor : BaseProcessor<Activity>
{
    internal const string VersionTag = "application.version";
    internal const string CommitTag = "application.commit";
    internal const string EnvironmentTag = "deployment.environment";

    private readonly string _version;
    private readonly string _commit;
    private readonly string _environment;

    public TelemetryEnrichmentProcessor(VersionResponse identity, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        _version = identity.Version;
        _commit = identity.Commit;
        _environment = environmentName;
    }

    public override void OnEnd(Activity activity)
    {
        activity.SetTag(VersionTag, _version);
        activity.SetTag(CommitTag, _commit);
        activity.SetTag(EnvironmentTag, _environment);
    }
}
