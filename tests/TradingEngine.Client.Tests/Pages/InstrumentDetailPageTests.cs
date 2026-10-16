using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Models;
using TradingEngine.Client.Pages;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.MonitoringRules;

namespace TradingEngine.Client.Tests.Pages;

[TestFixture]
public sealed class InstrumentDetailPageTests
{
    private static readonly Guid InstrumentA = Guid.Parse("d6000000-0000-0000-0000-000000000001");
    private static readonly Guid InstrumentB = Guid.Parse("d6000000-0000-0000-0000-000000000002");

    private static BunitContext CreateContext(StubHttpMessageHandler handler, ManualUiClock clock)
    {
        BunitContext ctx = new();
        HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost/") };
        ctx.Services.AddSingleton(new TradingEngineApiClient(http, TimeSpan.FromSeconds(30), []));
        ctx.Services.AddSingleton<IUiClock>(clock);
        ctx.Services.AddSingleton(new InstrumentListState());
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static void EnqueueInstrumentLoad(
        StubHttpMessageHandler handler,
        Guid instrumentId,
        string symbol,
        MonitoringRuleTimelineResponse timeline,
        MonitoringRuleDraftsResponse drafts)
    {
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, ClientTestData.JsonOf(ClientTestData.Settings(instrumentId, symbol))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK, ClientTestData.JsonOf(timeline)));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK, ClientTestData.JsonOf(drafts)));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"));
    }

    [Test]
    public async Task IdChange_PreviousResponseInFlight_DoesNotOverwriteNewInstrument()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        TaskCompletionSource<HttpResponseMessage> delayedA = new();
        handler.Enqueue((_, _) => delayedA.Task);
        EnqueueInstrumentLoad(handler, InstrumentB, "BSYM",
            ClientTestData.Timeline("tok-b"), ClientTestData.Drafts("tok-b"));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InstrumentDetailPage.Id)] = InstrumentB
            })));

        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("BSYM")),
            TimeSpan.FromSeconds(2));

        delayedA.SetResult(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentA, "ASYM"))));
        await Task.Delay(150);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("BSYM"));
            Assert.That(cut.Markup, Does.Not.Contain("ASYM"));
        });
    }

    [Test]
    public async Task Retry_AfterFailedInitialLoad_StartsBoundaryRefreshLoop()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        MonitoringRuleRevisionDto boundary = ClientTestData.Revision(
            Guid.NewGuid(),
            kind: "committed",
            revisionNumber: 2,
            effectiveFrom: clock.UtcNow + TimeSpan.FromMilliseconds(200));
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-1", boundary), ClientTestData.Drafts("tok-1"));
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-2", boundary), ClientTestData.Drafts("tok-2"));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Retry")),
            TimeSpan.FromSeconds(2));

        cut.Instance.RefreshInterval = TimeSpan.FromMilliseconds(10);
        cut.FindAll("button").Single(button => button.TextContent == "Retry").Click();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        cut.WaitForAssertion(
            () => Assert.That(handler.RequestCount, Is.GreaterThanOrEqualTo(9)),
            TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task BoundaryRefresh_WithModifiedPendingInput_PreservesInput()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        MonitoringRuleRevisionDto boundary = ClientTestData.Revision(
            Guid.NewGuid(),
            kind: "committed",
            revisionNumber: 2,
            effectiveFrom: clock.UtcNow + TimeSpan.FromMilliseconds(200));
        MonitoringRuleRevisionDto draft = ClientTestData.Revision(Guid.NewGuid());
        TaskCompletionSource<HttpResponseMessage> firstSettings = new();
        handler.Enqueue((_, _) => firstSettings.Task);
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-1", boundary))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Drafts("tok-1", draft))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"));
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-2", boundary), ClientTestData.Drafts("tok-2", draft));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));

        cut.Instance.RefreshInterval = TimeSpan.FromMilliseconds(10);
        firstSettings.SetResult(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentA, "XTEST"))));
        await Task.Delay(400);
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));
        await cut.InvokeAsync(() => cut.Instance.BeginSchedule(draft));
        cut.Instance.ActivePending!.To!.Set("2030-01-02T00:00:00");

        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        cut.WaitForAssertion(
            () => Assert.That(handler.RequestCount, Is.GreaterThanOrEqualTo(8)),
            TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.ActivePending, Is.Not.Null);
            Assert.That(cut.Instance.ActivePending!.To!.Modified, Is.True);
            Assert.That(cut.Instance.ActivePending.To!.Text, Is.EqualTo("2030-01-02T00:00:00"));
        });
    }

    [TestCase("server", "Background refresh failed. Your current input is kept.")]
    [TestCase("timeout", "Background refresh failed. Your current input is kept.")]
    [TestCase("unauthorized", "session may have expired")]
    [TestCase("notfound", "no longer exists or was deleted")]
    public async Task BoundaryRefresh_WhenLoadFails_KeepsEditorAndInput(
        string failure,
        string expectedMessage)
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        MonitoringRuleRevisionDto boundary = ClientTestData.Revision(
            Guid.NewGuid(),
            kind: "committed",
            revisionNumber: 2,
            effectiveFrom: clock.UtcNow + TimeSpan.FromMilliseconds(200));
        TaskCompletionSource<HttpResponseMessage> initial = new();
        handler.Enqueue((_, _) => initial.Task);
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-1", boundary))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Drafts("tok-1"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"));

        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.Instance.RefreshInterval = TimeSpan.FromMilliseconds(10);
        initial.SetResult(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentA, "XTEST"))));
        await Task.Delay(400);
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(5));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Settings").Click());
        cut.WaitForAssertion(
            () => Assert.That(
                cut.FindAll("input[aria-label='Sampling interval seconds']"), Has.Count.EqualTo(1)),
            TimeSpan.FromSeconds(2));
        await cut.InvokeAsync(
            () => cut.Find("input[aria-label='Sampling interval seconds']").Input("120"));

        handler.Fallback = failure switch
        {
            "server" => (_, _) => Task.FromResult(StubHttpMessageHandler.Json(
                HttpStatusCode.InternalServerError, """{"detail":"down"}""")),
            "timeout" => (_, _) => throw new TaskCanceledException(),
            "unauthorized" => (_, _) => Task.FromResult(StubHttpMessageHandler.Json(
                HttpStatusCode.Unauthorized, """{"detail":"expired"}""")),
            _ => (_, _) => Task.FromResult(StubHttpMessageHandler.Json(
                HttpStatusCode.NotFound, """{"detail":"gone"}"""))
        };
        Assert.That(cut.Instance.NextBoundary, Is.Not.Null);
        int requestsBefore = handler.RequestCount;

        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(600);
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.NextBoundary, Is.Not.Null);
            Assert.That(handler.RequestCount, Is.GreaterThan(requestsBefore));
        });
        cut.WaitForAssertion(
            () => Assert.That(cut.Instance.RefreshFaulted, Is.True),
            TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain(expectedMessage));
            Assert.That(cut.Markup, Does.Contain("Instrument settings"));
            Assert.That(cut.Markup, Does.Contain("value=\"120\""));
            Assert.That(cut.Markup, Does.Contain("Refresh now"));
        });
        if (failure == "unauthorized")
        {
            Assert.That(cut.Markup, Does.Contain("Sign in again"));
        }

        cut.Instance.RefreshInterval = TimeSpan.FromMinutes(5);
        handler.Fallback = null;
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-3", boundary), ClientTestData.Drafts("tok-3"));
        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Refresh now").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Instance.RefreshFaulted, Is.False),
            TimeSpan.FromSeconds(5));

        Assert.That(cut.Markup, Does.Contain("Instrument settings"));
    }

    [Test]
    public async Task PendingInput_WhenChanged_ImmediatelyBlocksInternalNavigation()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        MonitoringRuleRevisionDto draft = ClientTestData.Revision(Guid.NewGuid());
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-1"), ClientTestData.Drafts("tok-1", draft));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Drafts").Click());
        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Schedule").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Schedule draft")),
            TimeSpan.FromSeconds(2));
        Assert.That(cut.Instance.ActivePending!.HasInput, Is.False);

        await cut.InvokeAsync(
            () => cut.Find("input[aria-label='Effective from UTC']")
                .Change("2030-01-02T00:00:00"));

        Assert.That(cut.Instance.ActivePending!.HasInput, Is.True);

        NavigationManager nav = ctx.Services.GetRequiredService<NavigationManager>();
        string uriBefore = nav.Uri;
        nav.NavigateTo("http://localhost/instruments");
        await Task.Delay(150);

        Assert.Multiple(() =>
        {
            Assert.That(nav.Uri, Is.EqualTo(uriBefore));
            Assert.That(
                ctx.JSInterop.Invocations.Any(i => i.Identifier == "confirm"),
                Is.True);
        });
    }

    [Test]
    public async Task BeginApply_WhenPendingHasInput_KeepsExistingPendingAction()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        MonitoringRuleRevisionDto draft = ClientTestData.Revision(Guid.NewGuid());
        MonitoringRuleRevisionDto other = ClientTestData.Revision(Guid.NewGuid());
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-1"), ClientTestData.Drafts("tok-1", draft, other));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Drafts").Click());
        await cut.InvokeAsync(
            () => cut.FindAll("button").First(b => b.TextContent == "Schedule").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Schedule draft")),
            TimeSpan.FromSeconds(2));
        await cut.InvokeAsync(
            () => cut.Find("input[aria-label='Effective from UTC']")
                .Change("2030-01-02T00:00:00"));

        await cut.InvokeAsync(() => cut.Instance.BeginApply(other));

        Assert.Multiple(() =>
        {
            Assert.That(
                cut.Instance.ActivePending!.Kind,
                Is.EqualTo(PendingActionKind.Schedule));
            Assert.That(cut.Instance.ActivePending.Revision.Id, Is.EqualTo(draft.Id));
        });
    }

    [Test]
    public async Task SettingsConflict_WhenDiscarded_EditorAdoptsLatestAndSaveUsesNewToken()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-1"), ClientTestData.Drafts("tok-1"));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Settings").Click());
        cut.WaitForAssertion(
            () => Assert.That(
                cut.FindAll("input[aria-label='Sampling interval seconds']"), Has.Count.EqualTo(1)),
            TimeSpan.FromSeconds(2));

        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.PreconditionFailed,
            """{"detail":"stale","code":"watched_instrument.settings_concurrent_change"}"""));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(
                InstrumentA, "XTEST", interval: 90, token: "tok-2"))));

        await cut.InvokeAsync(
            () => cut.Find("input[aria-label='Sampling interval seconds']").Input("120"));
        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Save settings").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Server now shows")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(
                b => b.TextContent == "Use latest and discard my input").Click());
        await Task.Delay(100);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Server now shows"));
            Assert.That(cut.Markup, Does.Contain("value=\"90\""));
            Assert.That(cut.Markup, Does.Not.Contain("Unsaved changes"));
        });

        string? ifMatch = null;
        handler.Enqueue(async (request, _) =>
        {
            ifMatch = request.Headers.IfMatch?.ToString();
            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ClientTestData.JsonOf(ClientTestData.Settings(
                    InstrumentA, "XTEST", interval: 130, token: "tok-3")));
        });

        await cut.InvokeAsync(
            () => cut.Find("input[aria-label='Sampling interval seconds']").Input("130"));
        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Save settings").Click());
        cut.WaitForAssertion(
            () => Assert.That(ifMatch, Is.Not.Null),
            TimeSpan.FromSeconds(2));

        Assert.That(ifMatch, Does.Contain("tok-2"));
    }

    [Test]
    public async Task Apply_Confirm_ShowsOutcomeNoticeAndFocusForCommittedRevision()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        MonitoringRuleRevisionDto draft = ClientTestData.Revision(
            Guid.NewGuid(), changeReason: "widen bands");
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-1"), ClientTestData.Drafts("tok-1", draft));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));

        MonitoringRuleRevisionDto committed = ClientTestData.Revision(
            draft.Id,
            kind: "committed",
            revisionNumber: 3,
            effectiveFrom: clock.UtcNow);
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-2", committed))));
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-2", committed), ClientTestData.Drafts("tok-2"));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Drafts").Click());
        await cut.InvokeAsync(() => cut.Instance.BeginApply(draft));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Apply draft now")),
            TimeSpan.FromSeconds(2));
        await cut.InvokeAsync(
            () => cut.Find("section[aria-label='Apply draft now'] button.te-primary").Click());

        cut.WaitForAssertion(
            () => Assert.That(cut.Instance.Notice, Does.Contain("applied")),
            TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.Notice, Does.Contain("Revision 3"));
            Assert.That(cut.Instance.Notice, Does.Contain("earliest coverage period"));
            Assert.That(cut.Instance.FocusId, Is.EqualTo(draft.Id));
            Assert.That(cut.Instance.ActivePending, Is.Null);
            Assert.That(handler.Methods.Count(m => m == HttpMethod.Post), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Schedule_WithPastStart_ShowsErrorWithoutPosting()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        MonitoringRuleRevisionDto draft = ClientTestData.Revision(Guid.NewGuid());
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-1"), ClientTestData.Drafts("tok-1", draft));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Drafts").Click());
        await cut.InvokeAsync(() => cut.Instance.BeginSchedule(draft));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Schedule draft")),
            TimeSpan.FromSeconds(2));
        await cut.InvokeAsync(
            () => cut.Find("input[aria-label='Effective from UTC']")
                .Change("2020-01-01T00:00:00"));
        int requestsBefore = handler.RequestCount;

        await cut.InvokeAsync(
            () => cut.Find("section[aria-label='Schedule draft'] button.te-primary").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("already in the past")),
            TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(requestsBefore));
            Assert.That(cut.Instance.ActivePending, Is.Not.Null);
        });
    }

    [Test]
    public async Task DeleteDraft_Confirm_ShowsDeletionNotice()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        MonitoringRuleRevisionDto draft = ClientTestData.Revision(Guid.NewGuid());
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-1"), ClientTestData.Drafts("tok-1", draft));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));

        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-2"))));
        EnqueueInstrumentLoad(handler, InstrumentA, "XTEST",
            ClientTestData.Timeline("tok-2"), ClientTestData.Drafts("tok-2"));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Drafts").Click());
        await cut.InvokeAsync(() => cut.Instance.BeginDeleteDraft(draft));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Delete draft")),
            TimeSpan.FromSeconds(2));
        await cut.InvokeAsync(
            () => cut.Find("section[aria-label='Delete draft'] button.te-primary").Click());

        cut.WaitForAssertion(
            () => Assert.That(cut.Instance.Notice, Does.Contain("Draft deleted")),
            TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.Notice, Does.Contain("No committed revision changed"));
            Assert.That(cut.Instance.FocusId, Is.Null);
            Assert.That(handler.Methods.Count(m => m == HttpMethod.Delete), Is.EqualTo(1));
        });
    }

    [Test]
    public void PendingConfirm_WithoutTimeline_ShowsNoticeInsteadOfThrowing()
    {
        ManualUiClock clock = new(DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, ClientTestData.JsonOf(ClientTestData.Settings(InstrumentA, "XTEST"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, ClientTestData.JsonOf(ClientTestData.Drafts("tok-1"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"));
        using BunitContext ctx = CreateContext(handler, clock);
        IRenderedComponent<InstrumentDetailPage> cut = ctx.Render<InstrumentDetailPage>(
            parameters => parameters.Add(component => component.Id, InstrumentA));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("XTEST")),
            TimeSpan.FromSeconds(2));

        MonitoringRuleRevisionDto draft = ClientTestData.Revision(Guid.NewGuid());
        cut.InvokeAsync(() => cut.Instance.BeginSchedule(draft));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Instance.ActivePending, Is.Null);
            Assert.That(cut.Instance.Notice, Does.Contain("still loading"));
        });
    }
}
