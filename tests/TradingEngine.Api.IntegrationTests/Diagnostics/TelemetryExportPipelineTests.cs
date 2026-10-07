using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Contracts.Diagnostics;
using TradingEngine.Infrastructure.MonitoringRules.Xml;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class TelemetryExportPipelineTests
{
    private const string SyntheticQueryValue = "synthetic-marker-9f4d1b";
    private const string SyntheticHeaderValue = "synthetic-header-5c8e";
    private const string SyntheticCredential = "synthetic-credential-7e2a";
    private const string SyntheticXmlValue = "synthetic-private-rule-value";
    private const string SyntheticActivitySourceName = "TradingEngine.Tests.Synthetic";
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
                builder.UseSyntheticConnectionString();
                builder.UseSetting(
                    TelemetryServiceCollectionExtensions.ConnectionStringConfigurationKey,
                    $"InstrumentationKey={Guid.NewGuid()};" +
                    "IngestionEndpoint=https://localhost/");
                builder.ConfigureServices(services =>
                {
                    services.Configure<AzureMonitorOptions>(options =>
                    {
                        options.EnableLiveMetrics = false;
                        options.DisableOfflineStorage = true;
                    });
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
    public void Span_WhenRequestTelemetryCarriesSensitiveValues_ExportsScrubbedEnrichedSpan()
    {
        VersionResponse identity = new ApplicationVersionProvider().GetCurrent();
        using TracerProvider provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(SyntheticActivitySourceName)
            .AddProcessor(new TelemetryEnrichmentProcessor(identity, "Development"))
            .AddProcessor(new SensitiveDataTelemetryProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(_activities))
            .Build();

        using ActivitySource source = new(SyntheticActivitySourceName);
        using Activity? activity = source.StartActivity("GET /health", ActivityKind.Server);
        Assert.That(activity, Is.Not.Null);

        activity!.SetTag("url.full", $"http://localhost/health?probe={SyntheticQueryValue}");
        activity.SetTag("url.path", "/health");
        activity.SetTag("url.query", $"probe={SyntheticQueryValue}");
        activity.SetTag("http.request.header.x-synthetic", SyntheticHeaderValue);
        activity.SetTag("db.statement", $"SELECT '{SyntheticQueryValue}'");
        activity.Stop();

        Activity? span = _activities.Items.LastOrDefault(IsServerSpan);

        Assert.That(span, Is.Not.Null);

        foreach (KeyValuePair<string, object?> tag in span!.TagObjects)
        {
            Assert.That(tag.Value?.ToString(), Does.Not.Contain(SyntheticQueryValue));
            Assert.That(tag.Value?.ToString(), Does.Not.Contain(SyntheticHeaderValue));
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                span.GetTagItem("url.full"),
                Is.EqualTo("http://localhost/health"));
            Assert.That(
                span.GetTagItem("url.path"),
                Is.EqualTo("/health"));
            Assert.That(span.GetTagItem("db.statement"), Is.Null);
            Assert.That(span.GetTagItem("url.query"), Is.Null);
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
    public void Telemetry_WhenConfigured_DisablesExceptionEventsOnSpans()
    {
        _ = _factory.CreateClient();

        AspNetCoreTraceInstrumentationOptions aspNetCoreOptions = _factory.Services
            .GetRequiredService<IOptionsMonitor<AspNetCoreTraceInstrumentationOptions>>()
            .CurrentValue;
        HttpClientTraceInstrumentationOptions httpClientOptions = _factory.Services
            .GetRequiredService<IOptionsMonitor<HttpClientTraceInstrumentationOptions>>()
            .CurrentValue;

        Assert.Multiple(() =>
        {
            Assert.That(aspNetCoreOptions.RecordException, Is.False);
            Assert.That(httpClientOptions.RecordException, Is.False);
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
                record.Exception.ToString(),
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
    public void Startup_WhenNoTelemetryConnectionString_StillEmitsStartedEventToRegisteredProviders()
    {
        RecordingLoggerProvider recorder = new();

        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSyntheticConnectionString();
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

    private static bool IsServerSpan(Activity activity)
    {
        return activity.Kind == ActivityKind.Server;
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
