using System.Net;
using System.Net.Http.Json;
using TradingEngine.Contracts.MonitoringRules;
using static TradingEngine.Api.IntegrationTests.MonitoringRules.MonitoringRuleApiHost;

namespace TradingEngine.Api.IntegrationTests.MonitoringRules;

[TestFixture]
public sealed class MonitoringRuleTimestampEndpointTests
{
    private const string NanosecondInstant = "2030-01-01T00:00:00.000000001Z";
    private const string TickInstant = "2030-01-01T00:00:00.0000001Z";
    private const string LaterTickInstant = "2030-02-01T00:00:00.0000001Z";

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
    public async Task PostSchedule_WithNanosecondEffectiveFrom_RejectsAndLeavesStateUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            $$"""{"effectiveFrom":"{{NanosecondInstant}}","effectiveTo":null}""",
            token);

        await AssertRejectedUnchangedAsync(response, before, token);
    }

    [Test]
    public async Task PostSchedule_WithNanosecondEffectiveTo_RejectsAndLeavesStateUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            $$"""{"effectiveFrom":"2029-12-01T00:00:00Z","effectiveTo":"{{NanosecondInstant}}"}""",
            token);

        await AssertRejectedUnchangedAsync(response, before, token);
    }

    [Test]
    public async Task PostApply_WithNanosecondEffectiveTo_RejectsAndLeavesStateUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/apply"),
            $$"""{"effectiveTo":"{{NanosecondInstant}}"}""",
            token);

        await AssertRejectedUnchangedAsync(response, before, token);
    }

    [Test]
    public async Task PostDraft_WithNanosecondProposalDate_RejectsAndLeavesStateUnchanged()
    {
        string token = await _host.GetTokenAsync();
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path("/drafts"),
            $$"""
            {
              "changeReason": "proposal",
              "proposedPeriod": { "effectiveFrom": "{{NanosecondInstant}}", "effectiveTo": null },
              "definition": {{DefinitionJson(110m)}}
            }
            """,
            token);

        await AssertRejectedUnchangedAsync(response, before, token);
    }

    [Test]
    public async Task PutDraft_WithNanosecondProposalEnd_RejectsAndLeavesStateUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Put,
            Path($"/drafts/{draftId}"),
            $$"""
            {
              "changeReason": "proposal",
              "proposedPeriod": { "effectiveFrom": null, "effectiveTo": "{{NanosecondInstant}}" },
              "definition": {{DefinitionJson(115m)}}
            }
            """,
            token);

        await AssertRejectedUnchangedAsync(response, before, token);
    }

    [Test]
    public async Task PutRevision_WithNanosecondEffectiveFrom_RejectsAndLeavesStateUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        HttpResponseMessage scheduled = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest("2029-12-01T00:00:00Z", null),
            token);
        string currentToken = (await scheduled.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>())!.ConcurrencyToken;
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Put,
            Path($"/revisions/{draftId}"),
            $$"""{"effectiveFrom":"{{NanosecondInstant}}"}""",
            currentToken);

        await AssertRejectedUnchangedAsync(response, before, currentToken);
    }

    [Test]
    public async Task PostSchedule_WithTickPrecisionBoundaries_RoundTripsExactly()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            $$"""{"effectiveFrom":"{{TickInstant}}","effectiveTo":"{{LaterTickInstant}}"}""",
            token);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions[1].Id, Is.EqualTo(draftId));
            Assert.That(body.Revisions[1].EffectiveFrom, Is.EqualTo(DateTimeOffset.Parse(TickInstant)));
            Assert.That(body.Revisions[1].EffectiveTo, Is.EqualTo(DateTimeOffset.Parse(LaterTickInstant)));
            Assert.That(body.Revisions[2].EffectiveFrom, Is.EqualTo(DateTimeOffset.Parse(LaterTickInstant)));
        });
    }

    [Test]
    public async Task PostSchedule_WithOffsetTimestamp_ConvertsToUtcWithoutLoss()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            """{"effectiveFrom":"2030-01-01T01:00:00.5+01:00","effectiveTo":null}""",
            token);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                body!.Revisions[1].EffectiveFrom,
                Is.EqualTo(DateTimeOffset.Parse("2030-01-01T00:00:00.5Z")));
        });
    }

    [Test]
    public async Task GetApplicable_WithNanosecondInstant_ComparesAtFullPrecision()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest("2030-01-01T00:00:00Z", null),
            token);

        HttpResponseMessage justBefore = await _host.Client.GetAsync(
            Path("/revisions/applicable?at=2029-12-31T23:59:59.999999999Z"));
        HttpResponseMessage justAfter = await _host.Client.GetAsync(
            Path($"/revisions/applicable?at={NanosecondInstant}"));
        MonitoringRuleRevisionResponse? before = await justBefore.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();
        MonitoringRuleRevisionResponse? after = await justAfter.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(justBefore.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(justAfter.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(before!.Revision.Id, Is.EqualTo(InitialRevisionId));
            Assert.That(after!.Revision.Id, Is.EqualTo(draftId));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task PostSchedule_WithBlankEffectiveTo_RejectsAndPreservesFutureRevision(
        string effectiveTo)
    {
        (Guid greenId, _) = await ScheduleFutureRevisionAsync(120m);
        (Guid draftId, string currentToken) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            $$"""{"effectiveFrom":"2029-12-01T00:00:00Z","effectiveTo":"{{effectiveTo}}"}""",
            currentToken);

        await AssertInvalidUnchangedAsync(response, before, currentToken, greenId);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task PostApply_WithBlankEffectiveTo_RejectsAndPreservesFutureRevision(
        string effectiveTo)
    {
        (Guid greenId, _) = await ScheduleFutureRevisionAsync(120m);
        (Guid draftId, string currentToken) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/apply"),
            $$"""{"effectiveTo":"{{effectiveTo}}"}""",
            currentToken);

        await AssertInvalidUnchangedAsync(response, before, currentToken, greenId);
    }

    [Test]
    public async Task PostDraft_WithBlankProposalStart_RejectsAndLeavesStateUnchanged()
    {
        string token = await _host.GetTokenAsync();
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path("/drafts"),
            $$"""
            {
              "changeReason": "proposal",
              "proposedPeriod": { "effectiveFrom": "", "effectiveTo": null },
              "definition": {{DefinitionJson(110m)}}
            }
            """,
            token);

        await AssertInvalidUnchangedAsync(response, before, token, null);
    }

    [Test]
    public async Task PutDraft_WithBlankProposalEnd_RejectsAndLeavesStateUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Put,
            Path($"/drafts/{draftId}"),
            $$"""
            {
              "changeReason": "proposal",
              "proposedPeriod": { "effectiveFrom": null, "effectiveTo": " " },
              "definition": {{DefinitionJson(115m)}}
            }
            """,
            token);

        await AssertInvalidUnchangedAsync(response, before, token, null);
    }

    [Test]
    public async Task PutRevision_WithBlankEffectiveFrom_RejectsAndLeavesStateUnchanged()
    {
        (Guid revisionId, string token) = await ScheduleFutureRevisionAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Put,
            Path($"/revisions/{revisionId}"),
            """{"effectiveFrom":""}""",
            token);

        await AssertInvalidUnchangedAsync(response, before, token, revisionId);
    }

    [Test]
    public async Task GetApplicable_WithBlankAt_ReturnsInstantInvalid()
    {
        HttpResponseMessage response = await _host.Client.GetAsync(
            Path("/revisions/applicable?at="));
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.instant_invalid"));
        });
    }

    [Test]
    public async Task GetApplicable_WithWhitespaceAt_ReturnsInstantInvalid()
    {
        HttpResponseMessage response = await _host.Client.GetAsync(
            Path("/revisions/applicable?at=%20"));
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.instant_invalid"));
        });
    }

    [Test]
    public async Task GetApplicable_WithOmittedAt_ResolvesAtServerNow()
    {
        HttpResponseMessage response = await _host.Client.GetAsync(
            Path("/revisions/applicable"));
        MonitoringRuleRevisionResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleRevisionResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revision.Id, Is.EqualTo(InitialRevisionId));
        });
    }

    [Test]
    public async Task PostSchedule_WithMalformedTimestamp_ReturnsInstantInvalid()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            """{"effectiveFrom":"2030-13-45T99:00:00Z","effectiveTo":null}""",
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.instant_invalid"));
        });
    }

    private async Task<(Guid RevisionId, string Token)> ScheduleFutureRevisionAsync(
        decimal level)
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(level);
        HttpResponseMessage scheduled = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest("2029-12-01T00:00:00Z", null),
            token);
        Assert.That(scheduled.IsSuccessStatusCode, Is.True);
        MonitoringRuleTimelineResponse? body = await scheduled.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        return (draftId, body!.ConcurrencyToken);
    }

    private async Task AssertInvalidUnchangedAsync(
        HttpResponseMessage response,
        string before,
        string token,
        Guid? preservedRevisionId)
    {
        string? code = await ProblemCodeAsync(response);
        string after = await _host.SnapshotStateAsync();
        MonitoringRuleTimelineResponse timeline = await _host.GetTimelineAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.instant_invalid"));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(_host.Store.CurrentToken, Is.EqualTo(token));
        });

        if (preservedRevisionId is not null)
        {
            Assert.That(
                timeline.Revisions.Any(revision => revision.Id == preservedRevisionId),
                Is.True);
        }
    }

    private async Task AssertRejectedUnchangedAsync(
        HttpResponseMessage response,
        string before,
        string token)
    {
        string? code = await ProblemCodeAsync(response);
        string after = await _host.SnapshotStateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.instant_not_persistable"));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(_host.Store.CurrentToken, Is.EqualTo(token));
        });
    }

    private static string DefinitionJson(decimal level)
    {
        return $$"""
            {
              "priceScale": 4,
              "supportZones": [
                {
                  "id": "support-a",
                  "lower": {{level - 5m}},
                  "level": {{level}},
                  "upper": {{level + 5m}},
                  "conditions": [
                    { "type": "buy-zone", "actionId": "publish-signal" },
                    { "type": "support-loss", "actionId": "publish-signal" }
                  ]
                }
              ],
              "resistanceZones": []
            }
            """;
    }
}
