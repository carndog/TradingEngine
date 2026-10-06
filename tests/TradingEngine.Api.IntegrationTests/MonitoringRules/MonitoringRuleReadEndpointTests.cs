using System.Net;
using System.Net.Http.Json;
using TradingEngine.Contracts.MonitoringRules;
using static TradingEngine.Api.IntegrationTests.MonitoringRules.MonitoringRuleApiHost;

namespace TradingEngine.Api.IntegrationTests.MonitoringRules;

[TestFixture]
public sealed class MonitoringRuleReadEndpointTests
{
    private MonitoringRuleApiHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new MonitoringRuleApiHost();
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    [Test]
    public async Task GetTimeline_WithSeededRule_ReturnsRevisionsAndConcurrencyToken()
    {
        _host.SeedRule();

        HttpResponseMessage response = await _host.Client.GetAsync(Path());
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.ETag?.Tag, Is.EqualTo($"\"{_host.Store.CurrentToken}\""));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.MonitoringRuleId, Is.EqualTo(RuleId));
            Assert.That(body.WatchedInstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(body.ConcurrencyToken, Is.EqualTo(_host.Store.CurrentToken));
            Assert.That(body.CoverageOrigin, Is.EqualTo(January2.ToDateTimeOffset()));
            Assert.That(body.Revisions, Has.Count.EqualTo(1));
            Assert.That(body.Revisions[0].Id, Is.EqualTo(InitialRevisionId));
            Assert.That(body.Revisions[0].Kind, Is.EqualTo("committed"));
            Assert.That(body.Revisions[0].RevisionNumber, Is.EqualTo(1));
            Assert.That(body.Revisions[0].EffectiveTo, Is.Null);
            Assert.That(body.Revisions[0].CreatedBy, Is.EqualTo(SeedAuthor));
            Assert.That(Level(body.Revisions[0]), Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task GetTimeline_WithUnknownInstrument_ReturnsNotFoundProblem()
    {
        HttpResponseMessage response = await _host.Client.GetAsync(Path());
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(code, Is.EqualTo("monitoring_rule.not_found"));
        });
    }

    [Test]
    public async Task GetTimeline_WithDifferentInstrument_ReturnsNotFoundProblem()
    {
        _host.SeedRule();

        HttpResponseMessage response = await _host.Client.GetAsync(Path(Guid.NewGuid(), ""));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetRevision_WithKnownId_ReturnsRevisionAndToken()
    {
        _host.SeedRule();

        HttpResponseMessage response = await _host.Client.GetAsync(Path($"/revisions/{InitialRevisionId}"));
        MonitoringRuleRevisionResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revision.Id, Is.EqualTo(InitialRevisionId));
            Assert.That(body.ConcurrencyToken, Is.EqualTo(_host.Store.CurrentToken));
        });
    }

    [Test]
    public async Task GetRevision_WithUnknownId_ReturnsRevisionNotFound()
    {
        _host.SeedRule();

        HttpResponseMessage response = await _host.Client.GetAsync(Path($"/revisions/{Guid.NewGuid()}"));
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(code, Is.EqualTo("revision.not_found"));
        });
    }

    [Test]
    public async Task GetDrafts_WithDraft_ReturnsDraftSeparatelyFromTimeline()
    {
        _host.SeedRule();
        (Guid draftId, _) = await _host.CreateDraftAsync(110m);

        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();
        MonitoringRuleTimelineResponse timeline = await _host.GetTimelineAsync();

        Assert.Multiple(() =>
        {
            Assert.That(drafts.Drafts, Has.Count.EqualTo(1));
            Assert.That(drafts.Drafts[0].Id, Is.EqualTo(draftId));
            Assert.That(drafts.Drafts[0].Kind, Is.EqualTo("draft"));
            Assert.That(drafts.Drafts[0].RevisionNumber, Is.Null);
            Assert.That(drafts.Drafts[0].CreatedBy, Is.EqualTo("unverified-local-caller"));
            Assert.That(drafts.Drafts[0].CreatedAt, Is.EqualTo(Now.ToDateTimeOffset()));
            Assert.That(timeline.Revisions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GetApplicable_WithoutInstant_UsesInjectedClock()
    {
        _host.SeedRule();
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        await _host.SendAsync(HttpMethod.Post, Path($"/drafts/{draftId}/apply"), null, token);

        HttpResponseMessage response = await _host.Client.GetAsync(Path("/revisions/applicable"));
        MonitoringRuleRevisionResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revision.Id, Is.EqualTo(draftId));
            Assert.That(body.Revision.EffectiveFrom, Is.EqualTo(Now.ToDateTimeOffset()));
        });
    }

    [Test]
    public async Task GetApplicable_AtHistoricalInstant_ReturnsOriginalDefinitionAfterLaterChanges()
    {
        _host.SeedRule();
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        await _host.SendAsync(HttpMethod.Post, Path($"/drafts/{draftId}/apply"), null, token);

        HttpResponseMessage response = await _host.Client.GetAsync(
            Path("/revisions/applicable?at=2026-06-01T00:00:00Z"));
        MonitoringRuleRevisionResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(body!.Revision.Id, Is.EqualTo(InitialRevisionId));
            Assert.That(Level(body.Revision), Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task GetApplicable_AtSharedBoundary_SelectsSuccessor()
    {
        _host.SeedRule();
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest("2026-10-10T00:00:00Z", null),
            token);

        HttpResponseMessage atBoundary = await _host.Client.GetAsync(
            Path("/revisions/applicable?at=2026-10-10T00:00:00Z"));
        HttpResponseMessage justBefore = await _host.Client.GetAsync(
            Path("/revisions/applicable?at=2026-10-09T23:59:59.999999999Z"));
        MonitoringRuleRevisionResponse? boundary = await atBoundary.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();
        MonitoringRuleRevisionResponse? before = await justBefore.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(boundary!.Revision.Id, Is.EqualTo(draftId));
            Assert.That(before!.Revision.Id, Is.EqualTo(InitialRevisionId));
        });
    }

    [Test]
    public async Task GetApplicable_BeforeCoverageOrigin_ReturnsNoApplicableRevision()
    {
        _host.SeedRule();

        HttpResponseMessage response = await _host.Client.GetAsync(
            Path("/revisions/applicable?at=2025-01-01T00:00:00Z"));
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(code, Is.EqualTo("monitoring_rule.no_applicable_revision"));
        });
    }

    [Test]
    public async Task GetApplicable_WithInvalidInstant_ReturnsValidationProblem()
    {
        _host.SeedRule();

        HttpResponseMessage response = await _host.Client.GetAsync(
            Path("/revisions/applicable?at=not-an-instant"));
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.instant_invalid"));
        });
    }
}
