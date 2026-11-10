using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Testcontainers.MsSql;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class LocalOtlpSqlCorrelationTests
{
    private const string SyntheticQueryValue = "synthetic-prices-query-6b3d";
    private const string SqlClientSourceName = "OpenTelemetry.Instrumentation.SqlClient";
    private const string PricesRoute = "/api/watched-instruments/{id:guid}/price-observations/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] SqlTextTags =
    [
        "db.statement",
        "db.query.text"
    ];

    private MsSqlContainer _container = null!;
    private CapturingOtlpReceiver _receiver = null!;
    private CapturingExporter<Activity> _activities = null!;
    private HttpStatusCode _pricesStatus;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        string connectionString = _container.GetConnectionString();
        await MigrateAsync(connectionString);

        _receiver = new CapturingOtlpReceiver();
        _activities = new CapturingExporter<Activity>();
        CapturingExporter<Activity> activities = _activities;

        WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(ApiTestHost.ConnectionStringKey, connectionString);
                builder.UseSetting(
                    TelemetryServiceCollectionExtensions.OtlpEndpointConfigurationKey,
                    _receiver.BaseAddress.ToString().TrimEnd('/'));
                builder.UseSetting("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
                builder.ConfigureServices(services =>
                    services.ConfigureOpenTelemetryTracerProvider((_, tracerProviderBuilder) =>
                        tracerProviderBuilder.AddProcessor(
                            new SimpleActivityExportProcessor(activities))));
            });

        HttpClient client = factory.CreateClient();
        Guid instrumentId = await RegisterSyntheticInstrumentAsync(client);
        using HttpResponseMessage prices = await client.GetAsync(
            $"/api/watched-instruments/{instrumentId}/price-observations?pageSize=25&probe={SyntheticQueryValue}");
        _pricesStatus = prices.StatusCode;

        await factory.DisposeAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        _receiver.Dispose();
        await _container.DisposeAsync();
    }

    [Test]
    public void PricesRequest_WhenLocalOtlpSelected_ExportsSqlSpansAsChildrenOfRequestSpan()
    {
        Activity request = PricesRequestSpan();
        List<Activity> sqlSpans = SqlSpansIn(request);

        Assert.Multiple(() =>
        {
            Assert.That(_pricesStatus, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(sqlSpans, Is.Not.Empty);
            Assert.That(sqlSpans.Select(span => span.ParentSpanId), Is.All.EqualTo(request.SpanId));
            Assert.That(sqlSpans.Select(span => span.Kind), Is.All.EqualTo(ActivityKind.Client));
            Assert.That(
                sqlSpans.Select(span => span.GetTagItem("db.query.summary")),
                Is.All.Not.Null);
        });
    }

    [Test]
    public void PricesRequest_WhenLocalOtlpSelected_ExportsNoSqlTextOrQueryValues()
    {
        Activity request = PricesRequestSpan();
        List<Activity> exported = SqlSpansIn(request).Append(request).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(request.GetTagItem("url.query"), Is.Null);
            foreach (Activity span in exported)
            {
                foreach (string sqlTextTag in SqlTextTags)
                {
                    Assert.That(span.GetTagItem(sqlTextTag), Is.Null);
                }

                foreach (KeyValuePair<string, object?> tag in span.TagObjects)
                {
                    Assert.That(tag.Value?.ToString(), Does.Not.Contain(SyntheticQueryValue));
                    Assert.That(tag.Value?.ToString(), Does.Not.Contain("FROM "));
                    Assert.That(tag.Value?.ToString(), Does.Not.Contain("WHERE "));
                }
            }

            Assert.That(_receiver.AllPayloadText(), Does.Not.Contain(SyntheticQueryValue));
        });
    }

    private Activity PricesRequestSpan()
    {
        return _activities.Items.Single(activity =>
            activity.Kind == ActivityKind.Server
            && Equals(activity.GetTagItem("http.route"), PricesRoute));
    }

    private List<Activity> SqlSpansIn(Activity request)
    {
        return _activities.Items
            .Where(activity => activity.Source.Name == SqlClientSourceName)
            .Where(activity => activity.TraceId == request.TraceId)
            .ToList();
    }

    private static async Task MigrateAsync(string connectionString)
    {
        DbContextOptions<TradingEngineDbContext> options =
            new DbContextOptionsBuilder<TradingEngineDbContext>()
                .UseSqlServer(connectionString)
                .Options;
        await using TradingEngineDbContext context = new(options);
        await context.Database.MigrateAsync();
    }

    private static async Task<Guid> RegisterSyntheticInstrumentAsync(HttpClient client)
    {
        RegisterWatchedInstrumentRequest registration = new(
            "otlp-prices",
            "xtest",
            "gbp",
            60,
            "configured",
            4,
            [new ChartZoneDto(
                "support-a",
                95m,
                100m,
                105m,
                [
                    new ChartConditionDto("buy-zone", "publish-signal"),
                    new ChartConditionDto("support-loss", "publish-signal")
                ])],
            []);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            registration);
        response.EnsureSuccessStatusCode();
        WatchedInstrumentResponse? registered = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>(JsonOptions);

        return registered!.Id;
    }
}
