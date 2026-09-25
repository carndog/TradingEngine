using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class WatchedInstrumentEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private StubWatchedInstrumentStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new StubWatchedInstrumentStore();
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:TradingEngine"] =
                                "Server=localhost;Database=TradingEngineApiTests;Trusted_Connection=True;Encrypt=False"
                        }));
                builder.ConfigureServices(services =>
                    services.AddSingleton<IWatchedInstrumentStore>(_store));
            });
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task PostWatchedInstrument_WithValidRequest_ReturnsCreatedWithLocationAndBody()
    {
        HttpClient client = _factory.CreateClient();
        RegisterWatchedInstrumentRequest request = CreateRequest();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        WatchedInstrumentResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Id, Is.EqualTo(_store.GeneratedId));
            Assert.That(
                response.Headers.Location?.OriginalString,
                Is.EqualTo($"/api/watched-instruments/{body.Id}"));
            Assert.That(body.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(body.Exchange, Is.EqualTo("XTEST"));
            Assert.That(body.QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(body.MonitoringState, Is.EqualTo("configured"));
            Assert.That(body.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(body.PriceScale, Is.EqualTo(4));
            Assert.That(body.SupportZones, Has.Count.EqualTo(1));
            Assert.That(body.SupportZones[0].Id, Is.EqualTo("support-a"));
            Assert.That(body.SupportZones[0].Conditions![0].Type, Is.EqualTo("buy-zone"));
            Assert.That(body.ResistanceZones, Has.Count.EqualTo(1));
            Assert.That(body.ResistanceZones[0].Conditions![0].Type, Is.EqualTo("breakout"));
            Assert.That(body.CreatedAt, Is.EqualTo(body.LastChangedAt));
            Assert.That(_store.AddedRegistration, Is.Not.Null);
            Assert.That(
                _store.AddedRegistration!.MonitoringState,
                Is.EqualTo(MonitoringState.Configured));
        });
    }

    [Test]
    public async Task PostWatchedInstrument_WithMonitoredState_ReturnsMonitoredResponse()
    {
        HttpClient client = _factory.CreateClient();
        RegisterWatchedInstrumentRequest request = CreateRequest() with
        {
            MonitoringState = "monitored"
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        WatchedInstrumentResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.MonitoringState, Is.EqualTo("monitored"));
            Assert.That(
                _store.AddedRegistration!.MonitoringState,
                Is.EqualTo(MonitoringState.Monitored));
        });
    }

    [TestCase(0)]
    [TestCase(3601)]
    public async Task PostWatchedInstrument_WithInvalidSamplingInterval_ReturnsValidationProblem(
        int samplingIntervalSeconds)
    {
        HttpClient client = _factory.CreateClient();
        RegisterWatchedInstrumentRequest request = CreateRequest() with
        {
            SamplingIntervalSeconds = samplingIntervalSeconds
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(problem, Is.Not.Null);
            Assert.That(problem!.Status, Is.EqualTo(400));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.sampling_interval_out_of_range"));
            Assert.That(_store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task PostWatchedInstrument_WithInvalidZoneBoundaries_ReturnsValidationProblem()
    {
        HttpClient client = _factory.CreateClient();
        ChartZoneDto invalidZone = new(
            "support-a",
            105m,
            100m,
            95m,
            CreateSupportConditions());
        RegisterWatchedInstrumentRequest request = CreateRequest() with
        {
            SupportZones = [invalidZone]
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("chart_analysis.zone_invalid_boundary_order"));
            Assert.That(_store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task PostWatchedInstrument_WithUnknownConditionType_ReturnsValidationProblem()
    {
        HttpClient client = _factory.CreateClient();
        ChartZoneDto zone = new(
            "support-a",
            95m,
            100m,
            105m,
            [new ChartConditionDto("take-profit", "publish-signal")]);
        RegisterWatchedInstrumentRequest request = CreateRequest() with
        {
            SupportZones = [zone],
            ResistanceZones = []
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("chart_analysis.condition_type_undefined"));
            Assert.That(_store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task PostWatchedInstrument_WithUnknownMonitoringState_ReturnsValidationProblem()
    {
        HttpClient client = _factory.CreateClient();
        RegisterWatchedInstrumentRequest request = CreateRequest() with
        {
            MonitoringState = "paused"
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.monitoring_state_undefined"));
            Assert.That(_store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task PostWatchedInstrument_WithNullBody_ReturnsValidationProblem()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync<RegisterWatchedInstrumentRequest?>(
            "/api/watched-instruments",
            null);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.request_required"));
            Assert.That(_store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task PostWatchedInstrument_WithDuplicateBusinessKey_ReturnsConflictProblem()
    {
        _store.AddResult = WatchedInstrumentErrors.DuplicateBusinessKey;
        HttpClient client = _factory.CreateClient();
        RegisterWatchedInstrumentRequest request = CreateRequest();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.duplicate_business_key"));
        });
    }

    [Test]
    public async Task GetWatchedInstrument_WhenStored_ReturnsConfiguration()
    {
        WatchedInstrumentConfiguration configuration = CreateConfiguration();
        _store.GetResult = Result<WatchedInstrumentConfiguration>.Success(configuration);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments/{configuration.Instrument.Id}");
        WatchedInstrumentResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Id, Is.EqualTo(configuration.Instrument.Id));
            Assert.That(body.Symbol, Is.EqualTo("DEMO-2"));
            Assert.That(body.Exchange, Is.EqualTo("XTEST"));
            Assert.That(body.QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(body.MonitoringState, Is.EqualTo("configured"));
            Assert.That(body.SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(
                body.CreatedAt,
                Is.EqualTo(new DateTimeOffset(
                    Instant.FromUtc(2026, 1, 2, 9, 30).ToDateTimeUtc())));
            Assert.That(body.PriceScale, Is.EqualTo(4));
            Assert.That(body.SupportZones, Has.Count.EqualTo(1));
            Assert.That(body.SupportZones[0].Id, Is.EqualTo("support-a"));
            Assert.That(body.SupportZones[0].Lower, Is.EqualTo(95m));
            Assert.That(body.SupportZones[0].Conditions, Has.Count.EqualTo(2));
            Assert.That(body.SupportZones[0].Conditions![0].Type, Is.EqualTo("buy-zone"));
            Assert.That(
                body.SupportZones[0].Conditions![0].ActionId,
                Is.EqualTo("publish-signal"));
            Assert.That(body.ResistanceZones, Has.Count.EqualTo(1));
            Assert.That(body.ResistanceZones[0].Conditions![0].Type, Is.EqualTo("breakout"));
        });
    }

    [Test]
    public async Task GetWatchedInstrument_AfterCreate_ReturnsCreatedConfiguration()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            CreateRequest());
        WatchedInstrumentResponse? createdBody = await created.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>();
        _store.GetResult = CreateConfiguration(
            _store.GeneratedId,
            _store.AddedRegistration!);

        HttpResponseMessage response = await client.GetAsync(created.Headers.Location);
        WatchedInstrumentResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Id, Is.EqualTo(createdBody!.Id));
            Assert.That(body.Symbol, Is.EqualTo(createdBody.Symbol));
            Assert.That(body.Exchange, Is.EqualTo(createdBody.Exchange));
            Assert.That(body.QuoteCurrency, Is.EqualTo(createdBody.QuoteCurrency));
            Assert.That(body.MonitoringState, Is.EqualTo(createdBody.MonitoringState));
            Assert.That(body.SamplingIntervalSeconds, Is.EqualTo(createdBody.SamplingIntervalSeconds));
            Assert.That(body.CreatedAt, Is.EqualTo(createdBody.CreatedAt));
            Assert.That(body.SupportZones, Has.Count.EqualTo(1));
            Assert.That(body.SupportZones[0].Id, Is.EqualTo("support-a"));
            Assert.That(body.ResistanceZones, Has.Count.EqualTo(1));
            Assert.That(body.ResistanceZones[0].Id, Is.EqualTo("resistance-a"));
        });
    }

    [Test]
    public async Task GetWatchedInstrument_WithUnknownId_ReturnsNotFoundProblem()
    {
        HttpClient client = _factory.CreateClient();
        Guid unknownId = Guid.Parse("6c77c1f7-8b6c-4a0d-b6d7-1a2b3c4d5e6f");

        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments/{unknownId}");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.configuration_not_found"));
        });
    }

    private static RegisterWatchedInstrumentRequest CreateRequest()
    {
        return new RegisterWatchedInstrumentRequest(
            "demo-2",
            "xtest",
            "gbp",
            60,
            "configured",
            4,
            [new ChartZoneDto("support-a", 95m, 100m, 105m, CreateSupportConditions())],
            [new ChartZoneDto("resistance-a", 120m, 125m, 130m, CreateResistanceConditions())]);
    }

    private static ChartConditionDto[] CreateSupportConditions()
    {
        return
        [
            new ChartConditionDto("buy-zone", "publish-signal"),
            new ChartConditionDto("support-loss", "publish-signal")
        ];
    }

    private static ChartConditionDto[] CreateResistanceConditions()
    {
        return [new ChartConditionDto("breakout", "publish-signal")];
    }

    private static WatchedInstrumentConfiguration CreateConfiguration()
    {
        WatchedInstrument instrument = WatchedInstrument
            .Create(
                Guid.Parse("2f6f9c1a-3b7e-4d5a-9c8b-1a2b3c4d5e10"),
                "demo-2",
                "xtest",
                "gbp",
                60,
                Instant.FromUtc(2026, 1, 2, 9, 30))
            .Value;

        ChartAnalysisDefinition definition = ChartAnalysisDefinition.Create(
            4,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    95m,
                    100m,
                    105m,
                    [
                        ChartCondition.Create(
                            ChartConditionType.BuyZone,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                        ChartCondition.Create(
                            ChartConditionType.SupportLoss,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value
                    ]).Value
            ],
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("resistance-a").Value,
                    120m,
                    125m,
                    130m,
                    [
                        ChartCondition.Create(
                            ChartConditionType.Breakout,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value
                    ]).Value
            ]).Value;

        return new WatchedInstrumentConfiguration(instrument, definition);
    }

    private static WatchedInstrumentConfiguration CreateConfiguration(
        Guid id,
        WatchedInstrumentRegistration registration)
    {
        WatchedInstrument instrument = WatchedInstrument
            .Create(
                id,
                registration.Fields.Symbol,
                registration.Fields.Exchange,
                registration.Fields.QuoteCurrency,
                registration.Fields.SamplingIntervalSeconds,
                registration.CreatedAt)
            .Value;

        if (registration.MonitoringState == MonitoringState.Monitored)
        {
            instrument.StartMonitoring(
                registration.Fields.SamplingIntervalSeconds,
                registration.CreatedAt);
        }

        return new WatchedInstrumentConfiguration(instrument, registration.Definition);
    }

    private static string? ProblemCode(ProblemDetails? problem)
    {
        if (problem?.Extensions.TryGetValue("code", out object? code) is true
            && code is JsonElement element)
        {
            return element.GetString();
        }

        return null;
    }
}
