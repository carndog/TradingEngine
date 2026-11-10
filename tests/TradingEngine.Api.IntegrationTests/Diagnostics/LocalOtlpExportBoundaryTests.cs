using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingEngine.Api.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class LocalOtlpExportBoundaryTests
{
    private const string SyntheticQueryValue = "synthetic-otlp-query-4a7c";
    private const string SyntheticHeaderValue = "synthetic-otlp-header-8d2e";
    private const string SyntheticCredential = "synthetic-otlp-credential-1f9b";
    private const string HttpDependencyProbePath = "/http-dependency-probe";

    private static readonly TimeSpan ExportTimeout = TimeSpan.FromSeconds(30);

    private CapturingOtlpReceiver _receiver = null!;
    private CapturingTransport _azureTransport = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _receiver = new CapturingOtlpReceiver();
        _azureTransport = new CapturingTransport();
        CapturingTransport azureTransport = _azureTransport;

        WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSyntheticConnectionString();
                builder.UseSetting(
                    TelemetryServiceCollectionExtensions.OtlpEndpointConfigurationKey,
                    _receiver.BaseAddress.ToString().TrimEnd('/'));
                builder.UseSetting("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
                builder.UseSetting(
                    TelemetryServiceCollectionExtensions.ConnectionStringConfigurationKey,
                    $"InstrumentationKey={Guid.NewGuid()};IngestionEndpoint=https://localhost/");
                builder.ConfigureServices(services =>
                    services.Configure<AzureMonitorOptions>(options =>
                    {
                        options.EnableLiveMetrics = false;
                        options.DisableOfflineStorage = true;
                        options.Transport = azureTransport;
                    }));
            });

        HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, $"/health?probe={SyntheticQueryValue}");
        request.Headers.Add("X-Synthetic-Probe", SyntheticHeaderValue);
        using HttpResponseMessage response = await client.SendAsync(request);

        using HttpClient dependencyClient = new();
        using HttpResponseMessage dependencyResponse = await dependencyClient.GetAsync(
            new Uri(_receiver.BaseAddress, $"{HttpDependencyProbePath}?token={SyntheticCredential}"));

        ILogger<LocalOtlpExportBoundaryTests> logger = factory.Services
            .GetRequiredService<ILogger<LocalOtlpExportBoundaryTests>>();
        logger.LogError("Probe credential {Token}", SyntheticCredential);

        await factory.DisposeAsync();
        await Task.WhenAll(
            _receiver.WaitForPathAsync(CapturingOtlpReceiver.TracesPath, ExportTimeout),
            _receiver.WaitForPathAsync(CapturingOtlpReceiver.LogsPath, ExportTimeout),
            _receiver.WaitForPathAsync(CapturingOtlpReceiver.MetricsPath, ExportTimeout));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _receiver.Dispose();
    }

    [Test]
    public void Export_WhenLocalOtlpSelected_DeliversTracesLogsAndMetricsOverHttpProtobuf()
    {
        List<string> paths = _receiver.Requests.Select(request => request.Path).Distinct().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(paths, Does.Contain(CapturingOtlpReceiver.TracesPath));
            Assert.That(paths, Does.Contain(CapturingOtlpReceiver.LogsPath));
            Assert.That(paths, Does.Contain(CapturingOtlpReceiver.MetricsPath));
            Assert.That(
                _receiver.Requests
                    .Where(request => request.Path.StartsWith("/v1/", StringComparison.Ordinal))
                    .Select(request => request.ContentType),
                Is.All.EqualTo("application/x-protobuf"));
        });
    }

    [Test]
    public void Export_WhenLocalOtlpSelectedAlongsideAzureConnectionString_DoesNotExportToAzureMonitor()
    {
        Assert.That(_azureTransport.Requests, Is.Empty);
    }

    [Test]
    public void Export_WhenRequestHandled_ExportsEnrichedServerSpanWithServiceIdentity()
    {
        string traces = _receiver.PayloadText(CapturingOtlpReceiver.TracesPath);

        Assert.Multiple(() =>
        {
            Assert.That(traces, Does.Contain("TradingEngine.Api"));
            Assert.That(traces, Does.Contain("/health"));
            Assert.That(traces, Does.Contain(TelemetryEnrichmentProcessor.VersionTag));
            Assert.That(traces, Does.Contain(TelemetryEnrichmentProcessor.CommitTag));
            Assert.That(traces, Does.Contain(TelemetryEnrichmentProcessor.EnvironmentTag));
        });
    }

    [Test]
    public void Export_WhenHttpClientCalled_ExportsDependencySpanWithoutQueryString()
    {
        string traces = _receiver.PayloadText(CapturingOtlpReceiver.TracesPath);

        Assert.Multiple(() =>
        {
            Assert.That(traces, Does.Contain(HttpDependencyProbePath));
            Assert.That(traces, Does.Contain("System.Net.Http"));
        });
    }

    [Test]
    public void Export_WhenStartupCompletes_ExportsStartedEventLog()
    {
        string logs = _receiver.PayloadText(CapturingOtlpReceiver.LogsPath);

        Assert.That(logs, Does.Contain(StartupTelemetryHostedService.StartupEventName));
    }

    [Test]
    public void Export_WhenAnyTelemetryExported_ContainsNoSensitiveValue()
    {
        string allPayloads = _receiver.AllPayloadText();

        Assert.Multiple(() =>
        {
            Assert.That(allPayloads, Is.Not.Empty);
            Assert.That(allPayloads, Does.Not.Contain(SyntheticQueryValue));
            Assert.That(allPayloads, Does.Not.Contain(SyntheticHeaderValue));
            Assert.That(allPayloads, Does.Not.Contain(SyntheticCredential));
        });
    }
}
