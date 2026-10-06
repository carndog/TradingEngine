using System.Net;
using System.Net.Http.Json;
using TradingEngine.Contracts.MonitoringRules;
using static TradingEngine.Api.IntegrationTests.MonitoringRules.MonitoringRuleApiHost;

namespace TradingEngine.Api.IntegrationTests.MonitoringRules;

[TestFixture]
public sealed class MonitoringRuleConcurrencyEndpointTests
{
    private MonitoringRuleApiHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new MonitoringRuleApiHost();
        _host.SeedRule();
    }

    [TearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    [Test]
    public async Task PostDraft_WithoutIfMatch_ReturnsPreconditionRequiredWithoutDraft()
    {
        HttpResponseMessage response = await _host.Client.PostAsJsonAsync(
            Path("/drafts"),
            new MonitoringRuleDraftRequest("x", null, DefinitionDto(110m)));
        string? code = await ProblemCodeAsync(response);
        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)428));
            Assert.That(code, Is.EqualTo("monitoring_rule.concurrency_token_required"));
            Assert.That(drafts.Drafts, Is.Empty);
        });
    }

    [Test]
    public async Task PostDraft_WithMalformedIfMatch_ReturnsValidationProblem()
    {
        HttpRequestMessage request = new(HttpMethod.Post, Path("/drafts"))
        {
            Content = JsonContent.Create(new MonitoringRuleDraftRequest("x", null, DefinitionDto(110m)))
        };
        request.Headers.TryAddWithoutValidation("If-Match", "not-base64!!!");

        HttpResponseMessage response = await _host.Client.SendAsync(request);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.concurrency_token_invalid"));
        });
    }

    [Test]
    public async Task PostDraft_WithStaleIfMatch_Returns412AndLeavesWinningStateIntact()
    {
        string sharedToken = await _host.GetTokenAsync();
        HttpResponseMessage winner = await _host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest("winner", null, DefinitionDto(110m)),
            sharedToken);
        string winningToken = (await winner.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>())!.ConcurrencyToken;
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage loser = await _host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest("loser", null, DefinitionDto(120m)),
            sharedToken);
        string? code = await ProblemCodeAsync(loser);
        string after = await _host.SnapshotStateAsync();
        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(winner.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(loser.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
            Assert.That(code, Is.EqualTo("monitoring_rule.concurrent_change"));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(drafts.Drafts, Has.Count.EqualTo(1));
            Assert.That(drafts.Drafts[0].ChangeReason, Is.EqualTo("winner"));
            Assert.That(drafts.ConcurrencyToken, Is.EqualTo(winningToken));
        });
    }

    [Test]
    public async Task PostSchedule_WithStaleIfMatch_LeavesTimelineAndDraftUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        await _host.CreateDraftAsync(120m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest("2026-10-10T00:00:00Z", null),
            token);
        string after = await _host.SnapshotStateAsync();
        MonitoringRuleTimelineResponse timeline = await _host.GetTimelineAsync();
        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(timeline.Revisions, Has.Count.EqualTo(1));
            Assert.That(drafts.Drafts, Has.Count.EqualTo(2));
            Assert.That(drafts.Drafts.Single(draft => draft.Id == draftId).Kind, Is.EqualTo("draft"));
        });
    }

    [Test]
    public async Task DeleteRevision_WithStaleIfMatch_Returns412AndKeepsRevision()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        HttpResponseMessage scheduled = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest("2026-10-10T00:00:00Z", null),
            token);
        Assert.That(scheduled.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Delete,
            Path($"/revisions/{draftId}"),
            null,
            token);
        MonitoringRuleTimelineResponse timeline = await _host.GetTimelineAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
            Assert.That(timeline.Revisions, Has.Count.EqualTo(2));
            Assert.That(timeline.Revisions[1].Id, Is.EqualTo(draftId));
        });
    }

    [Test]
    public async Task PostSchedule_WithWeakAndUnquotedTokens_AreAccepted()
    {
        (Guid first, string token) = await _host.CreateDraftAsync(110m);
        HttpRequestMessage weak = new(HttpMethod.Post, Path($"/drafts/{first}/schedule"))
        {
            Content = JsonContent.Create(new ScheduleMonitoringRuleRequest("2026-10-10T00:00:00Z", null))
        };
        weak.Headers.TryAddWithoutValidation("If-Match", $"W/\"{token}\"");

        HttpResponseMessage weakResponse = await _host.Client.SendAsync(weak);
        (Guid second, string nextToken) = await _host.CreateDraftAsync(120m);
        HttpRequestMessage bare = new(HttpMethod.Post, Path($"/drafts/{second}/schedule"))
        {
            Content = JsonContent.Create(new ScheduleMonitoringRuleRequest("2026-10-20T00:00:00Z", null))
        };
        bare.Headers.TryAddWithoutValidation("If-Match", nextToken);
        HttpResponseMessage bareResponse = await _host.Client.SendAsync(bare);

        Assert.Multiple(() =>
        {
            Assert.That(weakResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(bareResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }
}
