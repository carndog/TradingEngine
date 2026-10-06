using System.Net;
using System.Net.Http.Json;
using NodaTime;
using TradingEngine.Contracts.MonitoringRules;
using static TradingEngine.Api.IntegrationTests.MonitoringRules.MonitoringRuleApiHost;

namespace TradingEngine.Api.IntegrationTests.MonitoringRules;

[TestFixture]
public sealed class MonitoringRuleLifecycleEndpointTests
{
    private const string Tuesday = "2026-10-06T00:00:00Z";
    private const string Wednesday = "2026-10-07T00:00:00Z";
    private const string Thursday = "2026-10-08T00:00:00Z";
    private static readonly DateTimeOffset TuesdayOffset = DateTimeOffset.Parse(Tuesday);
    private static readonly DateTimeOffset WednesdayOffset = DateTimeOffset.Parse(Wednesday);
    private static readonly DateTimeOffset ThursdayOffset = DateTimeOffset.Parse(Thursday);

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
    public async Task PostDraft_WithMatchingIfMatch_CreatesDraftAndRotatesToken()
    {
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest(
                "raise support",
                new RevisionPeriodDto(Tuesday, null),
                DefinitionDto(110m)),
            token);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();
        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.ConcurrencyToken, Is.Not.EqualTo(token));
            Assert.That(body.ConcurrencyToken, Is.EqualTo(_host.Store.CurrentToken));
            Assert.That(body.Revisions, Has.Count.EqualTo(1));
            Assert.That(drafts.Drafts, Has.Count.EqualTo(1));
            Assert.That(drafts.Drafts[0].ChangeReason, Is.EqualTo("raise support"));
            Assert.That(drafts.Drafts[0].ProposedEffectiveFrom, Is.EqualTo(TuesdayOffset));
            Assert.That(drafts.Drafts[0].ProposedEffectiveTo, Is.Null);
        });
    }

    [Test]
    public async Task PostDraft_Twice_KeepsBothDraftsIndependentOfTimeline()
    {
        await _host.CreateDraftAsync(110m);
        await _host.CreateDraftAsync(120m);

        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();
        MonitoringRuleTimelineResponse timeline = await _host.GetTimelineAsync();

        Assert.Multiple(() =>
        {
            Assert.That(drafts.Drafts, Has.Count.EqualTo(2));
            Assert.That(timeline.Revisions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task PutDraft_WithNewDefinitionAndProposal_ReplacesDraftContent()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Put,
            Path($"/drafts/{draftId}"),
            new MonitoringRuleDraftRequest(
                "revised",
                new RevisionPeriodDto(Tuesday, Thursday),
                DefinitionDto(115m)),
            token);
        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(drafts.Drafts, Has.Count.EqualTo(1));
            Assert.That(drafts.Drafts[0].Id, Is.EqualTo(draftId));
            Assert.That(Level(drafts.Drafts[0]), Is.EqualTo(115m));
            Assert.That(drafts.Drafts[0].ChangeReason, Is.EqualTo("revised"));
            Assert.That(drafts.Drafts[0].ProposedEffectiveFrom, Is.EqualTo(TuesdayOffset));
            Assert.That(drafts.Drafts[0].ProposedEffectiveTo, Is.EqualTo(ThursdayOffset));
        });
    }

    [Test]
    public async Task PutDraft_WithUnknownDraft_ReturnsRevisionNotFoundWithoutSave()
    {
        string token = await _host.GetTokenAsync();
        int saves = _host.Store.SaveCount;

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Put,
            Path($"/drafts/{Guid.NewGuid()}"),
            new MonitoringRuleDraftRequest("x", null, DefinitionDto(115m)),
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(code, Is.EqualTo("revision.not_found"));
            Assert.That(_host.Store.SaveCount, Is.EqualTo(saves));
            Assert.That(_host.Store.CurrentToken, Is.EqualTo(token));
        });
    }

    [Test]
    public async Task DeleteDraft_WithExistingDraft_RemovesOnlyThatDraft()
    {
        (Guid first, _) = await _host.CreateDraftAsync(110m);
        (Guid second, string token) = await _host.CreateDraftAsync(120m);

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Delete,
            Path($"/drafts/{first}"),
            null,
            token);
        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(drafts.Drafts.Select(draft => draft.Id), Is.EqualTo(new[] { second }));
        });
    }

    [Test]
    public async Task PostApply_WithoutBody_CommitsOpenEndedFromServerNow()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/apply"),
            null,
            token);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions, Has.Count.EqualTo(2));
            Assert.That(body.Revisions[0].Id, Is.EqualTo(InitialRevisionId));
            Assert.That(body.Revisions[0].EffectiveTo, Is.EqualTo(Now.ToDateTimeOffset()));
            Assert.That(body.Revisions[1].Id, Is.EqualTo(draftId));
            Assert.That(body.Revisions[1].EffectiveFrom, Is.EqualTo(Now.ToDateTimeOffset()));
            Assert.That(body.Revisions[1].EffectiveTo, Is.Null);
            Assert.That(body.Revisions[1].RevisionNumber, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task PostApply_WithEnd_CommitsTemporaryChangeAndContinuesCurrentDefinition()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/apply"),
            new ApplyMonitoringRuleRequest(Thursday),
            token);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions, Has.Count.EqualTo(3));
            Assert.That(body.Revisions[0].Id, Is.EqualTo(InitialRevisionId));
            Assert.That(body.Revisions[0].EffectiveTo, Is.EqualTo(Now.ToDateTimeOffset()));
            Assert.That(body.Revisions[1].Id, Is.EqualTo(draftId));
            Assert.That(body.Revisions[1].EffectiveFrom, Is.EqualTo(Now.ToDateTimeOffset()));
            Assert.That(body.Revisions[1].EffectiveTo, Is.EqualTo(ThursdayOffset));
            Assert.That(body.Revisions[2].Id, Is.Not.EqualTo(InitialRevisionId).And.Not.EqualTo(draftId));
            Assert.That(Level(body.Revisions[2]), Is.EqualTo(100m));
            Assert.That(body.Revisions[2].EffectiveFrom, Is.EqualTo(ThursdayOffset));
            Assert.That(body.Revisions[2].EffectiveTo, Is.Null);
            Assert.That(body.Revisions[2].CreatedAt, Is.EqualTo(Now.ToDateTimeOffset()));
        });
    }

    [Test]
    public async Task PostApply_WithEndCrossingFutureRevision_ContinuesWithDefinitionApplicableAtEnd()
    {
        await ScheduleAsync(120m, Wednesday, null);
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/apply"),
            new ApplyMonitoringRuleRequest(Thursday),
            token);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions, Has.Count.EqualTo(3));
            Assert.That(body.Revisions[1].Id, Is.EqualTo(draftId));
            Assert.That(body.Revisions[1].EffectiveTo, Is.EqualTo(ThursdayOffset));
            Assert.That(Level(body.Revisions[2]), Is.EqualTo(120m));
            Assert.That(body.Revisions[2].EffectiveFrom, Is.EqualTo(ThursdayOffset));
        });
    }

    [Test]
    public async Task PostApply_WithEndNotAfterNow_ReturnsInvalidPeriodAndLeavesStateUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/apply"),
            new ApplyMonitoringRuleRequest("2026-10-05T09:00:00Z"),
            token);
        string? code = await ProblemCodeAsync(response);
        string after = await _host.SnapshotStateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("revision.invalid_period"));
            Assert.That(after, Is.EqualTo(before));
        });
    }

    [Test]
    public async Task PostSchedule_ExampleOne_BoundedInsertIntoSingleRevision_CreatesContinuation()
    {
        (Guid b, HttpResponseMessage response) = await ScheduleAsync(120m, Tuesday, Thursday);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions, Has.Count.EqualTo(3));
            Assert.That(body.Revisions[0].Id, Is.EqualTo(InitialRevisionId));
            Assert.That(body.Revisions[0].EffectiveTo, Is.EqualTo(TuesdayOffset));
            Assert.That(body.Revisions[1].Id, Is.EqualTo(b));
            Assert.That(body.Revisions[1].EffectiveFrom, Is.EqualTo(TuesdayOffset));
            Assert.That(body.Revisions[1].EffectiveTo, Is.EqualTo(ThursdayOffset));
            Assert.That(Level(body.Revisions[2]), Is.EqualTo(100m));
            Assert.That(body.Revisions[2].EffectiveFrom, Is.EqualTo(ThursdayOffset));
            Assert.That(body.Revisions[2].EffectiveTo, Is.Null);
            Assert.That(body.Revisions[2].RevisionNumber, Is.EqualTo(3));
            Assert.That(body.Revisions.Select(revision => revision.Id).Distinct().Count(), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task PostSchedule_ExampleTwo_BoundedInsertSpanningBoundary_TrimsAndMovesSuccessor()
    {
        (Guid green, _) = await ScheduleAsync(120m, Wednesday, null);

        (Guid red, HttpResponseMessage response) = await ScheduleAsync(130m, Tuesday, Thursday);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions.Select(revision => revision.Id), Is.EqualTo(new[] { InitialRevisionId, red, green }));
            Assert.That(body.Revisions[0].EffectiveTo, Is.EqualTo(TuesdayOffset));
            Assert.That(body.Revisions[1].EffectiveTo, Is.EqualTo(ThursdayOffset));
            Assert.That(body.Revisions[2].EffectiveFrom, Is.EqualTo(ThursdayOffset));
            Assert.That(body.Revisions[2].EffectiveTo, Is.Null);
            Assert.That(Level(body.Revisions[2]), Is.EqualTo(120m));
        });
    }

    [Test]
    public async Task PostSchedule_ExampleThree_OpenEndedInsert_RemovesCoveredFutureRevision()
    {
        (Guid green, _) = await ScheduleAsync(120m, Wednesday, null);

        (Guid red, HttpResponseMessage response) = await ScheduleAsync(130m, Tuesday, null);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();
        HttpResponseMessage greenLookup = await _host.Client.GetAsync(Path($"/revisions/{green}"));

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions.Select(revision => revision.Id), Is.EqualTo(new[] { InitialRevisionId, red }));
            Assert.That(body.Revisions[1].EffectiveFrom, Is.EqualTo(TuesdayOffset));
            Assert.That(body.Revisions[1].EffectiveTo, Is.Null);
            Assert.That(greenLookup.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task PostSchedule_StartingBeforeNow_ReturnsBackdatedAndLeavesStateUnchanged()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest("2026-10-04T00:00:00Z", null),
            token);
        string? code = await ProblemCodeAsync(response);
        string after = await _host.SnapshotStateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("revision.backdated"));
            Assert.That(after, Is.EqualTo(before));
        });
    }

    [Test]
    public async Task PostSchedule_WithoutEffectiveFrom_ReturnsInstantInvalid()
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(110m);

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest(null, Thursday),
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.instant_invalid"));
        });
    }

    [Test]
    public async Task PutRevision_WithDefinitionAndStart_AmendsFutureRevision()
    {
        (Guid future, _) = await ScheduleAsync(120m, Wednesday, null);
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Put,
            Path($"/revisions/{future}"),
            new EditMonitoringRuleRevisionRequest("tuned", DefinitionDto(125m), Thursday),
            token);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions[0].EffectiveTo, Is.EqualTo(ThursdayOffset));
            Assert.That(body.Revisions[1].Id, Is.EqualTo(future));
            Assert.That(body.Revisions[1].EffectiveFrom, Is.EqualTo(ThursdayOffset));
            Assert.That(Level(body.Revisions[1]), Is.EqualTo(125m));
            Assert.That(body.Revisions[1].ChangeReason, Is.EqualTo("tuned"));
        });
    }

    [Test]
    public async Task PutRevision_WithEmptyBodyForUnknownRevision_ReturnsNoChangeRequestedWithoutSave()
    {
        string token = await _host.GetTokenAsync();
        int saves = _host.Store.SaveCount;

        HttpResponseMessage response = await _host.SendRawAsync(
            HttpMethod.Put,
            Path($"/revisions/{Guid.NewGuid()}"),
            "{}",
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("revision.no_change_requested"));
            Assert.That(_host.Store.SaveCount, Is.EqualTo(saves));
            Assert.That(_host.Store.CurrentToken, Is.EqualTo(token));
        });
    }

    [Test]
    public async Task PutRevision_WithReasonOnly_ReturnsNoChangeRequested()
    {
        (Guid future, _) = await ScheduleAsync(120m, Wednesday, null);
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Put,
            Path($"/revisions/{future}"),
            new EditMonitoringRuleRevisionRequest("reason only", null, null),
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("revision.no_change_requested"));
            Assert.That(_host.Store.CurrentToken, Is.EqualTo(token));
        });
    }

    [Test]
    public async Task PutRevision_WithDefinitionForUnknownRevision_ReturnsNotFoundWithoutSave()
    {
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Put,
            Path($"/revisions/{Guid.NewGuid()}"),
            new EditMonitoringRuleRevisionRequest(null, DefinitionDto(125m), null),
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(code, Is.EqualTo("revision.not_found"));
            Assert.That(_host.Store.CurrentToken, Is.EqualTo(token));
        });
    }

    [Test]
    public async Task PutRevision_WithValidDefinitionAndBackdatedStart_LeavesDefinitionAndTokenUnchanged()
    {
        (Guid future, _) = await ScheduleAsync(120m, Wednesday, null);
        string token = await _host.GetTokenAsync();
        string before = await _host.SnapshotStateAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Put,
            Path($"/revisions/{future}"),
            new EditMonitoringRuleRevisionRequest("combined", DefinitionDto(125m), "2026-10-04T00:00:00Z"),
            token);
        string? code = await ProblemCodeAsync(response);
        string after = await _host.SnapshotStateAsync();
        MonitoringRuleTimelineResponse timeline = await _host.GetTimelineAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("revision.backdated"));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(Level(timeline.Revisions[1]), Is.EqualTo(120m));
            Assert.That(timeline.ConcurrencyToken, Is.EqualTo(token));
        });
    }

    [Test]
    public async Task PutRevision_WithChangeReasonOver512Characters_ReturnsValidationProblem()
    {
        (Guid future, _) = await ScheduleAsync(120m, Wednesday, null);
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Put,
            Path($"/revisions/{future}"),
            new EditMonitoringRuleRevisionRequest(new string('r', 513), DefinitionDto(125m), null),
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.change_reason_too_long"));
            Assert.That(_host.Store.CurrentToken, Is.EqualTo(token));
        });
    }

    [Test]
    public async Task PostDraft_WithChangeReasonOver512Characters_ReturnsValidationProblemWithoutDraft()
    {
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest(new string('r', 513), null, DefinitionDto(110m)),
            token);
        string? code = await ProblemCodeAsync(response);
        MonitoringRuleDraftsResponse drafts = await _host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(code, Is.EqualTo("monitoring_rule.change_reason_too_long"));
            Assert.That(drafts.Drafts, Is.Empty);
        });
    }

    [Test]
    public async Task PutRevision_OnRevisionThatBeganAtExactlyNow_ReturnsPeriodBegun()
    {
        (Guid applied, string token) = await _host.CreateDraftAsync(110m);
        HttpResponseMessage apply = await _host.SendAsync(HttpMethod.Post, Path($"/drafts/{applied}/apply"), null, token);
        MonitoringRuleTimelineResponse? afterApply = await apply.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Put,
            Path($"/revisions/{applied}"),
            new EditMonitoringRuleRevisionRequest(null, DefinitionDto(125m), null),
            afterApply!.ConcurrencyToken);
        string? code = await ProblemCodeAsync(response);
        MonitoringRuleTimelineResponse timeline = await _host.GetTimelineAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(code, Is.EqualTo("revision.period_begun"));
            Assert.That(Level(timeline.Revisions[1]), Is.EqualTo(110m));
        });
    }

    [Test]
    public async Task DeleteRevision_FutureRevision_ReopensPredecessor()
    {
        (Guid future, _) = await ScheduleAsync(120m, Wednesday, null);
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Delete,
            Path($"/revisions/{future}"),
            null,
            token);
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Revisions, Has.Count.EqualTo(1));
            Assert.That(body.Revisions[0].Id, Is.EqualTo(InitialRevisionId));
            Assert.That(body.Revisions[0].EffectiveTo, Is.Null);
        });
    }

    [Test]
    public async Task DeleteRevision_CoverageOrigin_ReturnsProtectedConflict()
    {
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Delete,
            Path($"/revisions/{InitialRevisionId}"),
            null,
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(code, Is.EqualTo("revision.period_begun"));
            Assert.That(_host.Store.CurrentToken, Is.EqualTo(token));
        });
    }

    [Test]
    public async Task DeleteRevision_FutureOriginRevision_ReturnsCoverageOriginProtected()
    {
        _host.Dispose();
        _host = new MonitoringRuleApiHost();
        _host.SeedRule(Instant.FromUtc(2026, 10, 20, 0, 0));
        string token = await _host.GetTokenAsync();

        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Delete,
            Path($"/revisions/{InitialRevisionId}"),
            null,
            token);
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(code, Is.EqualTo("revision.coverage_origin_protected"));
        });
    }

    private async Task<(Guid DraftId, HttpResponseMessage Response)> ScheduleAsync(
        decimal level,
        string effectiveFrom,
        string? effectiveTo)
    {
        (Guid draftId, string token) = await _host.CreateDraftAsync(level);
        HttpResponseMessage response = await _host.SendAsync(
            HttpMethod.Post,
            Path($"/drafts/{draftId}/schedule"),
            new ScheduleMonitoringRuleRequest(effectiveFrom, effectiveTo),
            token);

        return (draftId, response);
    }
}
