using System.Diagnostics;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class TelemetryEnrichmentProcessorTests
{
    [Test]
    public void OnEnd_WhenActivityCompletes_AddsBuildIdentityAndEnvironmentTags()
    {
        VersionResponse identity = new("TradingEngine.Api", "0.1.0+abc1234", "abc1234");
        TelemetryEnrichmentProcessor processor = new(identity, "Development");
        Activity activity = new("GET /health");

        processor.OnEnd(activity);

        Assert.Multiple(() =>
        {
            Assert.That(
                activity.GetTagItem(TelemetryEnrichmentProcessor.VersionTag),
                Is.EqualTo("0.1.0+abc1234"));
            Assert.That(
                activity.GetTagItem(TelemetryEnrichmentProcessor.CommitTag),
                Is.EqualTo("abc1234"));
            Assert.That(
                activity.GetTagItem(TelemetryEnrichmentProcessor.EnvironmentTag),
                Is.EqualTo("Development"));
        });
    }
}
