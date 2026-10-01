using System.Diagnostics;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Infrastructure.MonitoringRules.Xml;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class AzureMonitorExportBoundaryTests
{
    private const string SyntheticCredential = "synthetic-credential-7e2a";
    private const string SyntheticXmlValue = "synthetic-private-rule-value";
    private const string CorrelationProbeMessage = "Correlation export probe.";
    private const string ExceptionProbeMessage = "Diagnostics exception probe.";
    private const string SchemaProbeMessage = "Persisted chart-analysis definition rejected.";
    private const string OriginalTypeProperty = "exception.original_type";
    private const string SanitizedDetailsProperty = "exception.sanitized_details";

    private CapturingTransport _transport = null!;
    private ActivityTraceId _correlationTraceId;
    private List<JsonElement> _exportedItems = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _transport = new CapturingTransport();
        CapturingTransport transport = _transport;

        WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    TelemetryServiceCollectionExtensions.ConnectionStringConfigurationKey,
                    $"InstrumentationKey={Guid.NewGuid()};IngestionEndpoint=https://localhost/");
                builder.ConfigureServices(services =>
                    services.Configure<AzureMonitorOptions>(options =>
                    {
                        options.EnableLiveMetrics = false;
                        options.DisableOfflineStorage = true;
                        options.Transport = transport;
                    }));
            });

        _ = factory.CreateClient();
        ILogger<AzureMonitorExportBoundaryTests> logger = factory.Services
            .GetRequiredService<ILogger<AzureMonitorExportBoundaryTests>>();

        EmitCorrelatedLog(logger);
        logger.LogError("Probe credential {Token}", SyntheticCredential);
        logger.LogError($"connection failed: password=\"{SyntheticCredential}\"");
        logger.LogError(ThrowSensitiveException(), ExceptionProbeMessage);
        logger.LogError(ThrowSchemaValidationException(), SchemaProbeMessage);

        await factory.DisposeAsync();
        await _transport.FirstRequestCaptured.WaitAsync(TimeSpan.FromSeconds(30));

        _exportedItems = ParseExportedItems();
    }

    [Test]
    public void Export_WhenHostStarted_DeliversStartedEventExactlyOnceWithBuildIdentity()
    {
        List<JsonElement> events = _exportedItems
            .Where(item => BaseType(item) == "EventData")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(
                events[0].GetProperty("data").GetProperty("baseData").GetProperty("name").GetString(),
                Is.EqualTo(StartupTelemetryHostedService.StartupEventName));
            Assert.That(Property(events[0], TelemetryEnrichmentProcessor.VersionTag), Is.Not.Null.Or.Empty);
            Assert.That(Property(events[0], TelemetryEnrichmentProcessor.CommitTag), Is.Not.Null.Or.Empty);
            Assert.That(
                Property(events[0], TelemetryEnrichmentProcessor.EnvironmentTag),
                Is.EqualTo("Development"));
        });
    }

    [Test]
    public void Export_WhenAnyTelemetryExported_ContainsNoSensitiveValue()
    {
        string allPayloads = string.Join("\n", _exportedItems.Select(item => item.GetRawText()));

        Assert.Multiple(() =>
        {
            Assert.That(_exportedItems, Is.Not.Empty);
            Assert.That(allPayloads, Does.Not.Contain(SyntheticCredential));
            Assert.That(allPayloads, Does.Not.Contain(SyntheticXmlValue));
        });
    }

    [Test]
    public void Export_WhenLogEmittedWithinSampledActivity_CarriesOperationId()
    {
        JsonElement item = _exportedItems.Single(candidate =>
            BaseType(candidate) == "MessageData"
            && Message(candidate) == CorrelationProbeMessage);

        Assert.That(
            item.GetProperty("tags").GetProperty("ai.operation.id").GetString(),
            Is.EqualTo(_correlationTraceId.ToString()));
    }

    [Test]
    public void Export_WhenSensitiveExceptionLogged_PreservesOriginalTypeAndFrames()
    {
        JsonElement item = _exportedItems.Single(candidate =>
            BaseType(candidate) == "ExceptionData"
            && Property(candidate, "OriginalFormat") == ExceptionProbeMessage);
        JsonElement details = item.GetProperty("data")
            .GetProperty("baseData")
            .GetProperty("exceptions")
            .EnumerateArray()
            .First();

        Assert.Multiple(() =>
        {
            Assert.That(details.GetProperty("message").GetString(), Does.Not.Contain(SyntheticCredential));
            Assert.That(
                details.GetProperty("typeName").GetString(),
                Is.EqualTo(typeof(TelemetrySanitizedException).FullName));
            Assert.That(
                Property(item, OriginalTypeProperty),
                Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(
                Property(item, SanitizedDetailsProperty),
                Does.Contain(nameof(ThrowSensitiveException)));
        });
    }

    [Test]
    public void Export_WhenSafeExceptionLogged_PreservesNativeTypeAndParsedStack()
    {
        JsonElement item = _exportedItems.Single(candidate =>
            BaseType(candidate) == "ExceptionData"
            && Property(candidate, "OriginalFormat") == SchemaProbeMessage);
        JsonElement details = item.GetProperty("data")
            .GetProperty("baseData")
            .GetProperty("exceptions")
            .EnumerateArray()
            .First();

        Assert.Multiple(() =>
        {
            Assert.That(
                details.GetProperty("typeName").GetString(),
                Is.EqualTo(typeof(System.Xml.Schema.XmlSchemaValidationException).FullName));
            Assert.That(details.GetProperty("message").GetString(), Does.Contain("'lower'"));
            Assert.That(details.GetProperty("parsedStack").GetArrayLength(), Is.GreaterThan(0));
            Assert.That(Property(item, OriginalTypeProperty), Is.Null);
        });
    }

    private void EmitCorrelatedLog(ILogger logger)
    {
        using Activity activity = new("export-correlation-probe");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        activity.Start();
        _correlationTraceId = activity.TraceId;
        logger.LogInformation(CorrelationProbeMessage);
        activity.Stop();
    }

    private List<JsonElement> ParseExportedItems()
    {
        List<JsonElement> items = [];
        foreach (CapturedRequest request in _transport.Requests)
        {
            if (request.Body is null)
            {
                continue;
            }

            string text = request.ContentEncoding == "gzip"
                ? DecompressGzip(request.Body)
                : request.Body;

            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                items.Add(JsonDocument.Parse(line).RootElement.Clone());
            }
        }

        return items;
    }

    private static string DecompressGzip(string base64Body)
    {
        using MemoryStream compressed = new(Convert.FromBase64String(base64Body));
        using System.IO.Compression.GZipStream gzip = new(
            compressed,
            System.IO.Compression.CompressionMode.Decompress);
        using StreamReader reader = new(gzip);
        return reader.ReadToEnd();
    }

    private static string? BaseType(JsonElement item)
    {
        return item.GetProperty("data").GetProperty("baseType").GetString();
    }

    private static string? Message(JsonElement item)
    {
        JsonElement baseData = item.GetProperty("data").GetProperty("baseData");
        return baseData.TryGetProperty("message", out JsonElement message)
            ? message.GetString()
            : null;
    }

    private static string? Property(JsonElement item, string key)
    {
        JsonElement baseData = item.GetProperty("data").GetProperty("baseData");
        if (baseData.TryGetProperty("properties", out JsonElement properties) is false)
        {
            return null;
        }

        return properties.TryGetProperty(key, out JsonElement value)
            ? value.GetString()
            : null;
    }

    private static InvalidOperationException ThrowSensitiveException()
    {
        try
        {
            throw new InvalidOperationException(
                $"authentication failed, token={SyntheticCredential}");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static Exception ThrowSchemaValidationException()
    {
        string xml =
            "<ChartAnalysisDefinition priceScale=\"4\"><SupportZones>" +
            $"<SupportZone id=\"support-a\" lower=\"{SyntheticXmlValue}\" level=\"100.0000\" upper=\"105.0000\">" +
            "<Condition type=\"buy-zone\" actionId=\"publish-signal\" />" +
            "</SupportZone></SupportZones></ChartAnalysisDefinition>";

        try
        {
            new ChartAnalysisDefinitionXmlSerializer().Deserialize(xml);
            throw new InvalidOperationException("Deserialize unexpectedly succeeded.");
        }
        catch (System.Xml.Schema.XmlSchemaValidationException exception)
        {
            return exception;
        }
    }
}
