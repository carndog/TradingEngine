using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Pages;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Tests.Pages;

[TestFixture]
public sealed class DraftEditorPageTests
{
    private static readonly Guid InstrumentId = Guid.Parse("d5000000-0000-0000-0000-000000000001");
    private static readonly Guid DraftId = Guid.Parse("d5000000-0000-0000-0000-0000000000d1");

    private static BunitContext CreateContext(StubHttpMessageHandler handler)
    {
        BunitContext ctx = new();
        HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost/") };
        ctx.Services.AddSingleton(new TradingEngineApiClient(http, TimeSpan.FromSeconds(30), []));
        ctx.Services.AddSingleton<IUiClock>(new ManualUiClock(
            DateTimeOffset.Parse("2026-10-05T09:00:00Z")));
        ctx.Services.AddSingleton(new InstrumentListState());
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static MonitoringRuleDefinitionDto Definition(
        int scale,
        decimal lower,
        decimal level,
        decimal upper)
    {
        return new MonitoringRuleDefinitionDto(
            scale,
            [new ChartZoneDto(
                "support-a",
                lower,
                level,
                upper,
                [new ChartConditionDto("buy-zone", "act-1"),
                 new ChartConditionDto("support-loss", "act-2")])],
            []);
    }

    private static MonitoringRuleRevisionDto Draft(
        MonitoringRuleDefinitionDto definition,
        string? reason = null,
        DateTimeOffset? proposedFrom = null)
    {
        return new MonitoringRuleRevisionDto(
            DraftId,
            "draft",
            null,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "synthetic-user",
            false,
            reason,
            null,
            null,
            proposedFrom,
            null,
            definition);
    }

    [Test]
    public async Task DraftConflict_WhenCompetingDefinitionChanged_SummaryShowsFieldDiffs()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, "XTEST"))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.RevisionResponse(
                "tok-d1", Draft(Definition(2, 90m, 95m, 100m), "mine")))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-t1"))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.PreconditionFailed,
            """{"detail":"stale","code":"monitoring_rule.draft_concurrent_change"}"""));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.RevisionResponse(
                "tok-d2", Draft(Definition(4, 80m, 95m, 110m), "competing change")))));

        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<DraftEditorPage> cut = ctx.Render<DraftEditorPage>(
            parameters => parameters
                .Add(component => component.Id, InstrumentId)
                .Add(component => component.DraftId, DraftId));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Save draft")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.Find("input[aria-label='Change reason']").Input("my reason"));
        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Save draft").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Latest draft:")),
            TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("reason 'competing change'"));
            Assert.That(cut.Markup, Does.Contain("price scale 2 → 4"));
            Assert.That(cut.Markup, Does.Contain("support zone 'support-a' bounds 90–100 around 95 → 80–110 around 95"));
            Assert.That(cut.Markup, Does.Contain("Your input is kept"));
        });
    }

    [Test]
    public async Task Save_TwiceAfterCreate_UpdatesSameDraftInsteadOfPostingAgain()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, "XTEST"))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-t0"))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.RevisionResponse(
                "tok-d1", Draft(Definition(2, 90m, 95m, 100m))))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-d2"))));

        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<DraftEditorPage> cut = ctx.Render<DraftEditorPage>(
            parameters => parameters.Add(component => component.Id, InstrumentId));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("New draft")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Save draft").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Instance.CreatedDraftId, Is.EqualTo(DraftId)),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Save draft").Click());
        cut.WaitForAssertion(
            () => Assert.That(handler.RequestCount, Is.EqualTo(4)),
            TimeSpan.FromSeconds(2));

        NavigationManager nav = ctx.Services.GetRequiredService<NavigationManager>();
        Assert.Multiple(() =>
        {
            Assert.That(
                handler.Methods,
                Is.EqualTo(new[]
                {
                    HttpMethod.Get, HttpMethod.Get, HttpMethod.Post, HttpMethod.Put
                }));
            Assert.That(nav.Uri, Does.Contain($"focus={DraftId}"));
            Assert.That(nav.Uri, Does.Contain("tab=drafts"));
        });
    }

    [Test]
    public async Task Save_WithNewlyEnteredPastProposedStart_ShowsErrorWithoutSaving()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, "XTEST"))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.RevisionResponse(
                "tok-d1", Draft(Definition(2, 90m, 95m, 100m))))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-t1"))));

        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<DraftEditorPage> cut = ctx.Render<DraftEditorPage>(
            parameters => parameters
                .Add(component => component.Id, InstrumentId)
                .Add(component => component.DraftId, DraftId));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Edit draft")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.Find("input[aria-label='Proposed effective from UTC']")
                .Change("2020-01-01T00:00:00"));
        int requestsBefore = handler.RequestCount;

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Save draft").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("is in the past")),
            TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestCount, Is.EqualTo(requestsBefore));
            Assert.That(cut.Markup, Does.Contain("Your input has been kept"));
        });
    }

    [Test]
    public async Task Apply_WithProposedStart_RequiresAcknowledgementBeforePosting()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, "XTEST"))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.RevisionResponse(
                "tok-d1",
                Draft(
                    Definition(2, 90m, 95m, 100m),
                    "widen bands",
                    DateTimeOffset.Parse("2026-10-06T00:00:00Z"))))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-t1"))));
        MonitoringRuleRevisionDto committed = ClientTestData.Revision(
            DraftId,
            kind: "committed",
            revisionNumber: 2,
            effectiveFrom: DateTimeOffset.Parse("2026-10-05T09:00:00Z"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-a1", committed))));

        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<DraftEditorPage> cut = ctx.Render<DraftEditorPage>(
            parameters => parameters
                .Add(component => component.Id, InstrumentId)
                .Add(component => component.DraftId, DraftId));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Edit draft")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Apply now").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Apply draft now")),
            TimeSpan.FromSeconds(2));

        int requestsBefore = handler.RequestCount;
        await cut.InvokeAsync(
            () => cut.Find("section[aria-label='Apply draft now'] button.te-primary").Click());
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("acknowledgement")),
            TimeSpan.FromSeconds(2));
        Assert.That(handler.RequestCount, Is.EqualTo(requestsBefore));

        await cut.InvokeAsync(
            () => cut.Find(
                "input[aria-label='Acknowledge that applying now replaces the proposed start']")
                .Change(true));
        await cut.InvokeAsync(
            () => cut.Find("section[aria-label='Apply draft now'] button.te-primary").Click());
        cut.WaitForAssertion(
            () => Assert.That(handler.RequestCount, Is.EqualTo(requestsBefore + 1)),
            TimeSpan.FromSeconds(2));

        NavigationManager nav = ctx.Services.GetRequiredService<NavigationManager>();
        Assert.Multiple(() =>
        {
            Assert.That(handler.Methods.Last(), Is.EqualTo(HttpMethod.Post));
            Assert.That(nav.Uri, Does.Contain("tab=current"));
            Assert.That(nav.Uri, Does.Contain($"focus={DraftId}"));
        });
    }

    [Test]
    public async Task Schedule_WithPastStart_ShowsErrorWithoutPosting()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, "XTEST"))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.RevisionResponse(
                "tok-d1", Draft(Definition(2, 90m, 95m, 100m))))));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Timeline("tok-t1"))));

        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<DraftEditorPage> cut = ctx.Render<DraftEditorPage>(
            parameters => parameters
                .Add(component => component.Id, InstrumentId)
                .Add(component => component.DraftId, DraftId));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Edit draft")),
            TimeSpan.FromSeconds(2));

        await cut.InvokeAsync(
            () => cut.FindAll("button").Single(b => b.TextContent == "Schedule").Click());
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

        Assert.That(handler.RequestCount, Is.EqualTo(requestsBefore));
    }
}
