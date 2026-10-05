using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Api.IntegrationTests.MonitoringRules;

[TestFixture]
public sealed class MonitoringRuleEndpointTests
{
    private static readonly Guid RuleId = Guid.Parse("d4000000-0000-0000-0000-000000000001");
    private static readonly Guid InstrumentId = Guid.Parse("d4000000-0000-0000-0000-000000000002");
    private static readonly Guid InitialRevisionId = Guid.Parse("d4000000-0000-0000-0000-000000000003");
    private static readonly Instant January2 = Instant.FromUtc(2026, 1, 2, 9, 30);
    private static readonly DateTimeOffset FutureStart = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private WebApplicationFactory<Program> _factory = null!;
    private StubMonitoringRuleStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new StubMonitoringRuleStore();
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
                    services.AddSingleton<IMonitoringRuleStore>(_store));
            });
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task GetTimeline_WithSeededRule_ReturnsRevisionsAndConcurrencyToken()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(TimelinePath(InstrumentId));
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.ETag?.Tag, Is.EqualTo($"\"{_store.CurrentToken}\""));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.MonitoringRuleId, Is.EqualTo(RuleId));
            Assert.That(body.WatchedInstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(body.ConcurrencyToken, Is.EqualTo(_store.CurrentToken));
            Assert.That(body.CoverageOrigin, Is.Not.Null);
            Assert.That(body.Revisions, Has.Count.EqualTo(1));
            Assert.That(body.Revisions[0].Id, Is.EqualTo(InitialRevisionId));
            Assert.That(body.Revisions[0].Kind, Is.EqualTo("committed"));
            Assert.That(body.Revisions[0].RevisionNumber, Is.EqualTo(1));
            Assert.That(body.Revisions[0].EffectiveTo, Is.Null);
            Assert.That(body.Revisions[0].Definition.SupportZones![0].Id, Is.EqualTo("support-a"));
        });
    }

    [Test]
    public async Task GetTimeline_WithUnknownInstrument_ReturnsNotFoundProblem()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(TimelinePath(InstrumentId));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(ProblemCode(problem), Is.EqualTo("monitoring_rule.not_found"));
        });
    }

    [Test]
    public async Task PostDraft_WithMatchingIfMatch_CreatesDraftAndReturnsTimeline()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();
        string token = await GetTokenAsync(client);

        HttpRequestMessage request = new(HttpMethod.Post, $"{TimelinePath(InstrumentId)}/drafts")
        {
            Content = JsonContent.Create(CreateDraftRequest())
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{token}\""));

        HttpResponseMessage response = await client.SendAsync(request);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.ConcurrencyToken, Is.EqualTo(_store.CurrentToken));
            Assert.That(body.ConcurrencyToken, Is.Not.EqualTo(token));
            Assert.That(body.Revisions, Has.Count.EqualTo(1));
        });

        HttpResponseMessage drafts = await client.GetAsync($"{TimelinePath(InstrumentId)}/drafts");
        MonitoringRuleDraftsResponse? draftsBody = await drafts.Content
            .ReadFromJsonAsync<MonitoringRuleDraftsResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(draftsBody, Is.Not.Null);
            Assert.That(draftsBody!.Drafts, Has.Count.EqualTo(1));
            Assert.That(draftsBody.Drafts[0].Kind, Is.EqualTo("draft"));
            Assert.That(draftsBody.Drafts[0].CreatedBy, Is.EqualTo("unverified-local-caller"));
            Assert.That(draftsBody.Drafts[0].ChangeReason, Is.EqualTo("raise support"));
        });
    }

    [Test]
    public async Task PostDraft_WithoutIfMatch_ReturnsPreconditionRequiredProblem()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"{TimelinePath(InstrumentId)}/drafts",
            CreateDraftRequest());
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)428));
            Assert.That(ProblemCode(problem), Is.EqualTo("monitoring_rule.concurrency_token_required"));
        });
    }

    [Test]
    public async Task PostDraft_WithMalformedIfMatch_ReturnsValidationProblem()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, $"{TimelinePath(InstrumentId)}/drafts")
        {
            Content = JsonContent.Create(CreateDraftRequest())
        };
        request.Headers.TryAddWithoutValidation("If-Match", "not-base64!!!");

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(ProblemCode(problem), Is.EqualTo("monitoring_rule.concurrency_token_invalid"));
        });
    }

    [Test]
    public async Task PostDraft_WithStaleIfMatch_ReturnsPreconditionFailedProblem()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();
        string stale = Convert.ToBase64String(new byte[] { 0xFF });
        HttpRequestMessage request = new(HttpMethod.Post, $"{TimelinePath(InstrumentId)}/drafts")
        {
            Content = JsonContent.Create(CreateDraftRequest())
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{stale}\""));

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
            Assert.That(ProblemCode(problem), Is.EqualTo("monitoring_rule.concurrent_change"));
        });
    }

    [Test]
    public async Task PostDraft_WithMissingDefinition_ReturnsValidationProblem()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();
        string token = await GetTokenAsync(client);
        MonitoringRuleDraftRequest request = new("raise support", null, null);
        HttpRequestMessage message = new(HttpMethod.Post, $"{TimelinePath(InstrumentId)}/drafts")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{token}\""));

        HttpResponseMessage response = await client.SendAsync(message);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(ProblemCode(problem), Is.EqualTo("monitoring_rule.definition_required"));
        });
    }

    [Test]
    public async Task PostSchedule_AfterDraftCreate_CommitsRevisionAtScheduledStart()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();
        string token = await GetTokenAsync(client);

        HttpRequestMessage create = new(HttpMethod.Post, $"{TimelinePath(InstrumentId)}/drafts")
        {
            Content = JsonContent.Create(CreateDraftRequest())
        };
        create.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{token}\""));
        await client.SendAsync(create);

        HttpResponseMessage drafts = await client.GetAsync($"{TimelinePath(InstrumentId)}/drafts");
        MonitoringRuleDraftsResponse? draftsBody = await drafts.Content
            .ReadFromJsonAsync<MonitoringRuleDraftsResponse>();
        Guid draftId = draftsBody!.Drafts[0].Id;

        string nextToken = draftsBody.ConcurrencyToken;
        HttpRequestMessage schedule = new(
            HttpMethod.Post,
            $"{TimelinePath(InstrumentId)}/drafts/{draftId}/schedule")
        {
            Content = JsonContent.Create(new ScheduleMonitoringRuleRequest(FutureStart, null))
        };
        schedule.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{nextToken}\""));

        HttpResponseMessage response = await client.SendAsync(schedule);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Revisions, Has.Count.EqualTo(2));
            Assert.That(body.Revisions[0].EffectiveTo, Is.EqualTo(FutureStart));
            Assert.That(body.Revisions[1].Id, Is.EqualTo(draftId));
            Assert.That(body.Revisions[1].Kind, Is.EqualTo("committed"));
            Assert.That(body.Revisions[1].EffectiveFrom, Is.EqualTo(FutureStart));
            Assert.That(body.Revisions[1].EffectiveTo, Is.Null);
            Assert.That(body.Revisions[1].RevisionNumber, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GetApplicable_WithValidInstant_ReturnsEffectiveRevision()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"{TimelinePath(InstrumentId)}/revisions/applicable?at=2026-01-02T09:30:00Z");
        MonitoringRuleRevisionResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Revision.Id, Is.EqualTo(InitialRevisionId));
            Assert.That(body.ConcurrencyToken, Is.EqualTo(_store.CurrentToken));
        });
    }

    [Test]
    public async Task GetApplicable_WithInvalidInstant_ReturnsValidationProblem()
    {
        _store.Seed(CreateRule());
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"{TimelinePath(InstrumentId)}/revisions/applicable?at=not-an-instant");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(ProblemCode(problem), Is.EqualTo("monitoring_rule.instant_invalid"));
        });
    }

    private async Task<string> GetTokenAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync(TimelinePath(InstrumentId));
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();
        Assert.That(body, Is.Not.Null);
        return body!.ConcurrencyToken;
    }

    private static string TimelinePath(Guid instrumentId)
    {
        return $"/api/watched-instruments/{instrumentId}/monitoring-rule";
    }

    private static MonitoringRuleDraftRequest CreateDraftRequest()
    {
        return new MonitoringRuleDraftRequest(
            "raise support",
            null,
            new MonitoringRuleDefinitionDto(
                4,
                [new ChartZoneDto("support-a", 105m, 110m, 115m, CreateSupportConditions())],
                []));
    }

    private static ChartConditionDto[] CreateSupportConditions()
    {
        return
        [
            new ChartConditionDto("buy-zone", "publish-signal"),
            new ChartConditionDto("support-loss", "publish-signal")
        ];
    }

    private static MonitoringRule CreateRule()
    {
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
            []).Value;

        return MonitoringRule.Create(
            RuleId,
            InstrumentId,
            InitialRevisionId,
            definition,
            January2,
            "seed-user").Value;
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
