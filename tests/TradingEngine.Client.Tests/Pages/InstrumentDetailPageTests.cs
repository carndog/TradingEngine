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
