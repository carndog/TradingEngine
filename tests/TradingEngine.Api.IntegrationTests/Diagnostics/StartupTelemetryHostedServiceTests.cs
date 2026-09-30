using TradingEngine.Api.Diagnostics;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class StartupTelemetryHostedServiceTests
{
    [Test]
    public async Task StartAsync_WhenServiceStarts_LogsOneCustomStartEventWithBuildIdentity()
    {
        RecordingLogger<StartupTelemetryHostedService> logger = new();
        StubHostEnvironment environment = new() { EnvironmentName = "Development" };
        ApplicationVersionProvider versionProvider = new();
        StartupTelemetryHostedService service = new(logger, versionProvider, environment);
        VersionResponse expected = versionProvider.GetCurrent();

        await service.StartAsync(CancellationToken.None);

        Assert.That(logger.States, Has.Count.EqualTo(1));
        IReadOnlyList<KeyValuePair<string, object?>> state = logger.States[0];
        Assert.Multiple(() =>
        {
            Assert.That(
                StateValue(state, "microsoft.custom_event.name"),
                Is.EqualTo(StartupTelemetryHostedService.StartupEventName));
            Assert.That(StateValue(state, "Application"), Is.EqualTo(expected.Application));
            Assert.That(StateValue(state, "Version"), Is.EqualTo(expected.Version));
            Assert.That(StateValue(state, "Commit"), Is.EqualTo(expected.Commit));
            Assert.That(StateValue(state, "Environment"), Is.EqualTo("Development"));
        });
    }

    private static object? StateValue(
        IReadOnlyList<KeyValuePair<string, object?>> state,
        string key)
    {
        foreach (KeyValuePair<string, object?> pair in state)
        {
            if (pair.Key == key)
            {
                return pair.Value;
            }
        }

        return null;
    }
}
