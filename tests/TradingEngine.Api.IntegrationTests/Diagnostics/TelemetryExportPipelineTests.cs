using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Infrastructure.MonitoringRules.Xml;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class TelemetryExportPipelineTests
{
    private const string SyntheticQueryValue = "synthetic-marker-9f4d1b";
    private const string SyntheticHeaderValue = "synthetic-header-5c8e";
    private const string SyntheticCredential = "synthetic-credential-7e2a";
    private const string SyntheticXmlValue = "synthetic-private-rule-value";
    private const string CustomEventNameAttribute = "microsoft.custom_event.name";

    private CapturingExporter<Activity> _activities = null!;
    private CapturingLogRecordExporter _logRecords = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _activities = new CapturingExporter<Activity>();
        _logRecords = new CapturingLogRecordExporter();
        CapturingExporter<Activity> activities = _activities;
        CapturingLogRecordExporter logRecords = _logRecords;

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            [TelemetryServiceCollectionExtensions.ConnectionStringConfigurationKey] =
                                "InstrumentationKey=00000000-0000-0000-0000-000000000000;" +
                                "IngestionEndpoint=https://localhost/"
                        }));
                builder.ConfigureServices(services =>
                {
                    services.ConfigureOpenTelemetryTracerProvider((_, tracerProviderBuilder) =>
                        tracerProviderBuilder.AddProcessor(
                            new SimpleActivityExportProcessor(activities)));
                    services.ConfigureOpenTelemetryLoggerProvider((_, loggerProviderBuilder) =>
                        loggerProviderBuilder.AddProcessor(
                            new SimpleLogRecordExportProcessor(logRecords)));
                });
            });
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public void Startup_WhenHostStarted_ExportsStartedEventExactlyOnceAfterExportPipelineReady()
    {
        _ = _factory.CreateClient();

        List<CapturedLogRecord> startedEvents = _logRecords.Items
            .Where(HasStartedEventName)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(startedEvents, Has.Count.EqualTo(1));
            Assert.That(
                AttributeValue(startedEvents[0], TelemetryEnrichmentProcessor.VersionTag),
                Is.Not.Null.Or.Empty);
            Assert.That(
                AttributeValue(startedEvents[0], TelemetryEnrichmentProcessor.CommitTag),
                Is.Not.Null.Or.Empty);
            Assert.That(
                AttributeValue(startedEvents[0], TelemetryEnrichmentProcessor.EnvironmentTag),
                Is.EqualTo("Development"));
        });
    }

    [Test]
    public async Task Request_WhenSensitiveQueryAndHeaderPresent_ExportsSpanWithoutSensitiveValues()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(
            HttpMethod.Get,
            $"/health?probe={SyntheticQueryValue}");
        request.Headers.Add("X-Test-Synthetic", SyntheticHeaderValue);

        using HttpResponseMessage response = await client.SendAsync(request);

        Activity? span = _activities.Items
            .LastOrDefault(activity =>
                activity.DisplayName.Contains("health", StringComparison.OrdinalIgnoreCase)
                || activity.TagObjects.Any(tag =>
                    tag.Value?.ToString()?.Contains("/health") is true));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                span,
                Is.Not.Null,
                $"Captured {_activities.Items.Count} activity(s): " +
                $"{string.Join(", ", _activities.Items.Select(activity => activity.DisplayName))}");
        });

        foreach (KeyValuePair<string, object?> tag in span!.TagObjects)
        {
            Assert.That(tag.Value?.ToString(), Does.Not.Contain(SyntheticQueryValue));
            Assert.That(tag.Value?.ToString(), Does.Not.Contain(SyntheticHeaderValue));
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                span.GetTagItem(TelemetryEnrichmentProcessor.VersionTag),
                Is.Not.Null.Or.Empty);
            Assert.That(
                span.GetTagItem(TelemetryEnrichmentProcessor.CommitTag),
                Is.Not.Null.Or.Empty);
            Assert.That(
                span.GetTagItem(TelemetryEnrichmentProcessor.EnvironmentTag),
                Is.EqualTo("Development"));
        });
    }

    [Test]
    public void Log_WhenEmittedWithinActivity_ExportsEnrichedCorrelatedRecord()
    {
        _ = _factory.CreateClient();
        ILogger<TelemetryExportPipelineTests> logger = _factory.Services
            .GetRequiredService<ILogger<TelemetryExportPipelineTests>>();

        using Activity activity = new("test-correlation");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        logger.LogInformation("Synthetic correlation probe.");

        CapturedLogRecord record = _logRecords.Items
            .Last(record => record.CategoryName == typeof(TelemetryExportPipelineTests).FullName);

        Assert.Multiple(() =>
        {
            Assert.That(record.TraceId, Is.EqualTo(activity.TraceId));
            Assert.That(record.SpanId, Is.EqualTo(activity.SpanId));
            Assert.That(
                AttributeValue(record, TelemetryEnrichmentProcessor.VersionTag),
                Is.Not.Null.Or.Empty);
            Assert.That(
                AttributeValue(record, TelemetryEnrichmentProcessor.CommitTag),
                Is.Not.Null.Or.Empty);
            Assert.That(
                AttributeValue(record, TelemetryEnrichmentProcessor.EnvironmentTag),
                Is.EqualTo("Development"));
        });
    }

    [Test]
    public void Log_WhenSensitivePatternsPresent_ExportsRedactedMessageAndAttributes()
    {
        _ = _factory.CreateClient();
        ILogger<TelemetryExportPipelineTests> logger = _factory.Services
            .GetRequiredService<ILogger<TelemetryExportPipelineTests>>();

        logger.LogError(
            "Diagnostics probe failed {Detail}",
            $"Authorization: Bearer {SyntheticCredential}");

        CapturedLogRecord record = _logRecords.Items
            .Last(record => record.CategoryName == typeof(TelemetryExportPipelineTests).FullName);

        Assert.Multiple(() =>
        {
            Assert.That(record.FormattedMessage, Does.Not.Contain(SyntheticCredential));
            Assert.That(record.FormattedMessage, Does.Contain("Diagnostics probe failed"));
            Assert.That(
                record.Attributes!.Any(attribute =>
                    attribute.Value?.ToString()?.Contains(SyntheticCredential) is true),
                Is.False);
        });
    }

    [Test]
    public void Log_WhenExceptionMessageContainsCredential_ExportsSanitizedException()
    {
        _ = _factory.CreateClient();
        ILogger<TelemetryExportPipelineTests> logger = _factory.Services
            .GetRequiredService<ILogger<TelemetryExportPipelineTests>>();

        InvalidOperationException leaking = new(
            $"authentication failed, token={SyntheticCredential}");

        logger.LogError(leaking, "Diagnostics exception probe.");

        CapturedLogRecord record = _logRecords.Items
            .Last(record => record.CategoryName == typeof(TelemetryExportPipelineTests).FullName);

        Assert.Multiple(() =>
        {
            Assert.That(record.Exception, Is.TypeOf<TelemetrySanitizedException>());
            Assert.That(
                record.Exception!.ToString(),
                Does.Not.Contain(SyntheticCredential));
            Assert.That(
                record.Exception.Message,
                Does.Contain(nameof(InvalidOperationException)));
        });
    }

    [Test]
    public void Log_WhenSchemaValidationExceptionLogged_ExportsSafeDiagnosticsWithoutDocumentValues()
    {
        _ = _factory.CreateClient();
        ChartAnalysisDefinitionXmlSerializer serializer = new();
        string xml =
            "<ChartAnalysisDefinition priceScale=\"4\">" +
            "<SupportZones><SupportZone id=\"support-a\" lower=\"95.0000\" level=\"100.0000\" upper=\"105.0000\">" +
            $"<Condition type=\"{SyntheticXmlValue}\" actionId=\"publish-signal\" />" +
            "</SupportZone></SupportZones></ChartAnalysisDefinition>";

        Exception? thrown = null;
        try
        {
            serializer.Deserialize(xml);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        ILogger<TelemetryExportPipelineTests> logger = _factory.Services
            .GetRequiredService<ILogger<TelemetryExportPipelineTests>>();
        logger.LogError(thrown!, "Persisted chart-analysis definition rejected.");

        CapturedLogRecord record = _logRecords.Items
            .Last(record => record.CategoryName == typeof(TelemetryExportPipelineTests).FullName);

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.TypeOf<System.Xml.Schema.XmlSchemaValidationException>());
            Assert.That(record.Exception, Is.SameAs(thrown));
            Assert.That(record.Exception!.ToString(), Does.Not.Contain(SyntheticXmlValue));
            Assert.That(record.Exception.StackTrace, Is.Not.Null.Or.Empty);
            Assert.That(
                record.FormattedMessage,
                Does.Contain("Persisted chart-analysis definition rejected."));
        });
    }

    [Test]
    public void Startup_WhenNoConnectionString_StillEmitsStartedEventToRegisteredProviders()
    {
        RecordingLoggerProvider recorder = new();

        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                    services.AddSingleton<ILoggerProvider>(recorder));
            });

        _ = factory.CreateClient();

        Assert.That(
            recorder.States.Any(state =>
                state.Any(pair =>
                    pair.Key == CustomEventNameAttribute
                    && pair.Value?.ToString() == StartupTelemetryHostedService.StartupEventName)),
            Is.True);
    }

    private static bool HasStartedEventName(CapturedLogRecord record)
    {
        return record.Attributes is not null
            && record.Attributes.Any(attribute =>
                attribute.Key == CustomEventNameAttribute
                && attribute.Value?.ToString() == StartupTelemetryHostedService.StartupEventName);
    }

    private static string? AttributeValue(CapturedLogRecord record, string key)
    {
        return record.Attributes?
            .Where(attribute => attribute.Key == key)
            .Select(attribute => attribute.Value?.ToString())
            .FirstOrDefault();
    }
}
