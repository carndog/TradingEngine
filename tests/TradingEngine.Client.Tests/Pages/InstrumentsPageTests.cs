using System.Net;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Pages;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Tests.Pages;

[TestFixture]
public sealed class InstrumentsPageTests
{
    private static readonly Guid FirstId = Guid.Parse("d7000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondId = Guid.Parse("d7000000-0000-0000-0000-000000000002");
    private static readonly Guid ThirdId = Guid.Parse("d7000000-0000-0000-0000-000000000003");

    private static BunitContext CreateContext(StubHttpMessageHandler handler)
    {
        BunitContext ctx = new();
        HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost/") };
        ctx.Services.AddSingleton(new TradingEngineApiClient(http, TimeSpan.FromSeconds(30), []));
        ctx.Services.AddSingleton(new InstrumentListState());
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static WatchedInstrumentSummaryDto Item(Guid id, string symbol, string token)
    {
        return new WatchedInstrumentSummaryDto(
            id, symbol, "XTST", "USD", "configured", 60, token);
    }

    private static string PageJson(
        int page,
        int pageSize,
        int total,
        params WatchedInstrumentSummaryDto[] items)
    {
        return ClientTestData.JsonOf(new WatchedInstrumentPageResponse(
            items, page, pageSize, total));
    }

    private static IRenderedComponent<InstrumentsPage> RenderPage(
        BunitContext ctx,
        StubHttpMessageHandler handler,
        string searchBody,
        Action<ComponentParameterCollectionBuilder<InstrumentsPage>>? parameters = null)
    {
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK, searchBody));
        IRenderedComponent<InstrumentsPage> cut = parameters is null
            ? ctx.Render<InstrumentsPage>()
            : ctx.Render<InstrumentsPage>(parameters);
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Delete selected")),
            TimeSpan.FromSeconds(2));
        return cut;
    }

    private static IElement RowCheckbox(IRenderedComponent<InstrumentsPage> cut, Guid id)
    {
        return cut.FindAll("tbody input[type='checkbox']")
            .Single(input => input.GetAttribute("aria-label")!
                .Contains(id == FirstId ? "AAA" : id == SecondId ? "BBB" : "CCC"));
    }

    private static async Task WaitForMarkupAsync(
        IRenderedComponent<InstrumentsPage> cut,
        string expected,
        int timeoutMilliseconds = 5000)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (cut.Markup.Contains(expected))
            {
                return;
            }

            await Task.Delay(20);
            await cut.InvokeAsync(() => { });
        }

        Assert.That(cut.Markup, Does.Contain(expected));
    }

    [Test]
    public async Task RowCheckbox_SelectingRow_ShowsSelectionStatus()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = RenderPage(ctx, handler,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b")));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(
                cut.Find("thead input[type='checkbox']").GetAttribute("aria-label"),
                Is.EqualTo("Select all instruments on this page"));
            Assert.That(
                RowCheckbox(cut, FirstId).GetAttribute("aria-label"),
                Is.EqualTo("Select AAA on XTST in USD"));
        });

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);

        Assert.That(cut.Markup, Does.Contain("1 selected on this page"));
        Assert.That(
            ctx.JSInterop.Invocations.Count(i => i.Identifier == "teSetIndeterminate"),
            Is.GreaterThan(0));
    }

    [Test]
    public async Task HeaderCheckbox_OnPartialPage_SelectsOnlyVisibleRows()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);
        ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/instruments?page=2&pageSize=10");
        IRenderedComponent<InstrumentsPage> cut = RenderPage(ctx, handler,
            PageJson(2, 10, 11, Item(ThirdId, "CCC", "tok-c")));

        cut.Find("thead input[type='checkbox']").Change(true);
        await Task.Delay(50);

        Assert.That(cut.Markup, Does.Contain("1 selected on this page"));

        cut.Find("thead input[type='checkbox']").Change(false);
        await Task.Delay(50);

        Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
    }

    [Test]
    public async Task PageChange_WithSelection_ClearsSelection()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 15, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(2, 10, 15, Item(ThirdId, "CCC", "tok-c"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        Assert.That(cut.Markup, Does.Contain("1 selected on this page"));

        ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/instruments?page=2&pageSize=10");
        await Task.Delay(150);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(cut.Markup, Does.Contain("CCC"));
        });
    }

    [Test]
    public async Task SearchChange_WhileConfirmationOpen_CancelsConfirmationAndClearsSelection()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 1, Item(ThirdId, "CCC", "tok-c"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);
        Assert.That(cut.Markup, Does.Contain("Delete 1 instrument?"));

        ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/instruments?q=CCC");
        await Task.Delay(150);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Delete 1 instrument?"));
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
        });
    }

    [Test]
    public async Task SearchInput_BeforeDebounceCompletes_ClearsSelectionAndConfirmation()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 1, Item(ThirdId, "CCC", "tok-c"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(5));

        RowCheckbox(cut, FirstId).Change(true);
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("1 selected on this page")),
            TimeSpan.FromSeconds(5));
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Delete 1 instrument?")),
            TimeSpan.FromSeconds(5));

        await cut.InvokeAsync(() =>
        {
            cut.Find("input[type='search']").Input("CC");
            Assert.Multiple(() =>
            {
                Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
                Assert.That(cut.Markup, Does.Not.Contain("Delete 1 instrument?"));
                Assert.That(handler.RequestCount, Is.EqualTo(1));
            });
        });

        await WaitForMarkupAsync(cut, "CCC");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(cut.Markup, Does.Not.Contain("Delete 1 instrument?"));
        });
    }

    [Test]
    public async Task SearchInput_BeforeDebounceCompletes_BlocksReselectionAndDeletion()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 1, Item(ThirdId, "CCC", "tok-c"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(5));

        RowCheckbox(cut, FirstId).Change(true);
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("1 selected on this page")),
            TimeSpan.FromSeconds(5));

        await cut.InvokeAsync(() =>
        {
            cut.Find("input[type='search']").Input("CC");

            IElement rowBox = RowCheckbox(cut, FirstId);
            Assert.Multiple(() =>
            {
                Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
                Assert.That(rowBox.HasAttribute("disabled"), Is.True);
                Assert.That(
                    cut.Find("thead input[type='checkbox']").HasAttribute("disabled"),
                    Is.True);
                Assert.That(
                    cut.FindAll("button")
                        .Single(b => b.TextContent == "Delete selected")
                        .HasAttribute("disabled"),
                    Is.True);
                Assert.That(handler.RequestCount, Is.EqualTo(1));
            });

            rowBox.Change(true);
            cut.Find("thead input[type='checkbox']").Change(true);
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));

            Assert.That(
                cut.FindAll("button").Count(b => b.TextContent == "Delete 1 instrument"),
                Is.EqualTo(0));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
        });

        await WaitForMarkupAsync(cut, "CCC");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(cut.Markup, Does.Not.Contain("Delete 1 instrument?"));
        });
    }

    [Test]
    public async Task SearchInput_SameUrlWhileLoadInFlight_ReloadsAndRestoresSelection()
    {
        StubHttpMessageHandler handler = new();
        TaskCompletionSource<HttpResponseMessage> gated = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue((_, _) => gated.Task);
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 1, Item(ThirdId, "CCC", "tok-c"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(5));

        await cut.InvokeAsync(() => cut.Find("input[type='search']").Input("AA"));
        await WaitForRequestCountAsync(cut, handler, 2);

        await cut.InvokeAsync(() => cut.Find("input[type='search']").Input("AA "));
        await WaitForRequestCountAsync(cut, handler, 3);

        gated.SetResult(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 0)));

        await WaitForMarkupAsync(cut, "CCC");
        await WaitForRenderSettleAsync(cut);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(RowCheckbox(cut, ThirdId).HasAttribute("disabled"), Is.False);
        });

        await cut.InvokeAsync(() => RowCheckbox(cut, ThirdId).Change(true));
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("1 selected on this page")),
            TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ConfirmDelete_WithSelection_PostsIdsAndTokensThenReloads()
    {
        StubHttpMessageHandler handler = new();
        string? postedBody = null;
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue(async (request, _) =>
        {
            postedBody = await request.Content!.ReadAsStringAsync();
            return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                ClientTestData.JsonOf(new BulkDeleteWatchedInstrumentsResponse(
                    [FirstId], [])));
        });
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 1, Item(SecondId, "BBB", "tok-b"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Delete 1 instrument?"));
            Assert.That(cut.Markup, Does.Contain("AAA · XTST · USD"));
            Assert.That(cut.Markup, Does.Contain("history"));
        });

        cut.FindAll("button").Single(b => b.TextContent == "Delete 1 instrument").Click();
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(postedBody, Is.Not.Null);
            Assert.That(postedBody, Does.Contain(FirstId.ToString()));
            Assert.That(postedBody, Does.Contain("tok-a"));
            Assert.That(cut.Markup, Does.Contain("Deleted 1 instrument."));
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(handler.Methods.Count(m => m == HttpMethod.Post), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConfirmDelete_WhenCancelled_KeepsSelectionAndClosesPanel()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = RenderPage(ctx, handler,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b")));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);

        cut.FindAll("button").Single(b => b.TextContent == "Cancel").Click();
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Delete 1 instrument?"));
            Assert.That(cut.Markup, Does.Contain("1 selected on this page"));
            Assert.That(handler.Methods.Any(m => m == HttpMethod.Post), Is.False);
        });
    }

    [Test]
    public async Task ConfirmDelete_WhenTokenStale_RefreshesAndRequiresReselection()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed,
            """{"detail":"stale","code":"watched_instrument.delete_items_stale"}"""));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a2"), Item(SecondId, "BBB", "tok-b2"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete 1 instrument").Click();
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Delete 1 instrument?"));
            Assert.That(cut.Markup, Does.Contain("reselect"));
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(handler.Methods.Count(m => m == HttpMethod.Post), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConfirmDelete_WhenNetworkFails_KeepsConfirmationAndSelection()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue((_, _) => throw new HttpRequestException("offline"));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete 1 instrument").Click();
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Delete 1 instrument?"));
            Assert.That(cut.Markup, Does.Contain("1 selected on this page"));
        });
    }

    [Test]
    public async Task ConfirmDelete_WhenClickedTwice_SendsSingleRequest()
    {
        StubHttpMessageHandler handler = new();
        TaskCompletionSource<HttpResponseMessage> gated = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 2, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue((_, _) => gated.Task);
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 1, Item(SecondId, "BBB", "tok-b"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);
        IElement confirm = cut.FindAll("button")
            .Single(b => b.TextContent == "Delete 1 instrument");
        confirm.Click();
        confirm.Click();
        await Task.Delay(50);

        gated.SetResult(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            ClientTestData.JsonOf(new BulkDeleteWatchedInstrumentsResponse([FirstId], []))));
        await Task.Delay(200);

        Assert.That(handler.Methods.Count(m => m == HttpMethod.Post), Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteSuccess_OnLastRowOfLastPage_ReturnsToPageOne()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(2, 1, 2, Item(SecondId, "BBB", "tok-b"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            ClientTestData.JsonOf(new BulkDeleteWatchedInstrumentsResponse([SecondId], []))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 1, 1, Item(FirstId, "AAA", "tok-a"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 10, 1, Item(FirstId, "AAA", "tok-a"))));
        using BunitContext ctx = CreateContext(handler);
        ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/instruments?page=2&pageSize=10");
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("BBB")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, SecondId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete 1 instrument").Click();
        await Task.Delay(200);

        NavigationManager nav = ctx.Services.GetRequiredService<NavigationManager>();
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Deleted 1 instrument."));
            Assert.That(cut.Markup, Does.Contain("AAA"));
            Assert.That(nav.Uri, Does.EndWith("/instruments?pageSize=10"));
        });
    }

    [Test]
    public async Task DeleteSuccess_RemovingAllMatches_ShowsEmptyState()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 1, Item(FirstId, "AAA", "tok-a"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            ClientTestData.JsonOf(new BulkDeleteWatchedInstrumentsResponse([FirstId], []))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 25, 0)));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete 1 instrument").Click();
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Deleted 1 instrument."));
            Assert.That(cut.Markup, Does.Contain("No instruments found"));
        });
    }

    [Test]
    public async Task PageChange_WithDelayedLoad_SelectionClearedBeforeNewPageArrives()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 10, 11, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        TaskCompletionSource<HttpResponseMessage> gated = new();
        handler.Enqueue((_, _) => gated.Task);
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(2, 10, 11, Item(ThirdId, "CCC", "tok-c"))));
        using BunitContext ctx = CreateContext(handler);
        ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/instruments?pageSize=10");
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(5));

        RowCheckbox(cut, FirstId).Change(true);
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("1 selected on this page")),
            TimeSpan.FromSeconds(5));

        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Delete 1 instrument?")),
            TimeSpan.FromSeconds(5));

        cut.FindAll("button").Single(b => b.TextContent == "Next").Click();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("0 selected on this page")),
            TimeSpan.FromSeconds(5));

        Assert.That(cut.Markup, Does.Not.Contain("Delete 1 instrument?"));

        gated.SetResult(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(2, 10, 11, Item(ThirdId, "CCC", "tok-c"))));
        await WaitForMarkupAsync(cut, "CCC");

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(
                handler.Methods.Count(m => m == HttpMethod.Post),
                Is.EqualTo(0));
        });
    }

    [Test]
    public async Task NavigationChange_DuringInFlightDelete_DoesNotOverwriteNewPageState()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(1, 10, 11, Item(FirstId, "AAA", "tok-a"), Item(SecondId, "BBB", "tok-b"))));
        TaskCompletionSource<HttpResponseMessage> gatedDelete = new();
        handler.Enqueue((_, _) => gatedDelete.Task);
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            PageJson(2, 10, 11, Item(ThirdId, "CCC", "tok-c"))));
        using BunitContext ctx = CreateContext(handler);
        ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/instruments?pageSize=10");
        IRenderedComponent<InstrumentsPage> cut = ctx.Render<InstrumentsPage>();
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("AAA")),
            TimeSpan.FromSeconds(2));

        RowCheckbox(cut, FirstId).Change(true);
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete selected").Click();
        await Task.Delay(50);
        cut.FindAll("button").Single(b => b.TextContent == "Delete 1 instrument").Click();
        await Task.Delay(50);

        ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/instruments?page=2&pageSize=10");
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("CCC")),
            TimeSpan.FromSeconds(5));

        int rendersBefore = cut.RenderCount;
        gatedDelete.SetResult(StubHttpMessageHandler.Json(HttpStatusCode.OK,
            ClientTestData.JsonOf(new BulkDeleteWatchedInstrumentsResponse([FirstId], []))));
        await WaitForRenderAsync(cut, rendersBefore);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("CCC"));
            Assert.That(cut.Markup, Does.Contain("0 selected on this page"));
            Assert.That(cut.Markup, Does.Not.Contain("Deleted 1 instrument."));
            Assert.That(cut.Markup, Does.Not.Contain("Delete 1 instrument?"));
            Assert.That(
                handler.Methods.Count(m => m == HttpMethod.Post),
                Is.EqualTo(1));
        });
    }

    private static async Task WaitForRequestCountAsync(
        IRenderedComponent<InstrumentsPage> cut,
        StubHttpMessageHandler handler,
        int expected,
        int timeoutMilliseconds = 5000)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (handler.RequestCount >= expected)
            {
                return;
            }

            await Task.Delay(20);
            await cut.InvokeAsync(() => { });
        }

        Assert.That(handler.RequestCount, Is.GreaterThanOrEqualTo(expected));
    }

    private static async Task WaitForRenderSettleAsync(
        IRenderedComponent<InstrumentsPage> cut,
        int timeoutMilliseconds = 5000)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        int stableReads = 0;
        int lastRenderCount = cut.RenderCount;
        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(50);
            await cut.InvokeAsync(() => { });
            int renderCount = cut.RenderCount;
            stableReads = renderCount == lastRenderCount ? stableReads + 1 : 0;
            lastRenderCount = renderCount;
            if (stableReads >= 3)
            {
                return;
            }
        }

        Assert.That(stableReads, Is.GreaterThanOrEqualTo(3));
    }

    private static async Task WaitForRenderAsync(
        IRenderedComponent<InstrumentsPage> cut,
        int rendersBefore,
        int timeoutMilliseconds = 5000)
    {
        long deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (cut.RenderCount > rendersBefore)
            {
                return;
            }

            await Task.Delay(20);
            await cut.InvokeAsync(() => { });
        }

        Assert.That(cut.RenderCount, Is.GreaterThan(rendersBefore));
    }

    [Test]
    public void DeleteButton_WithNoSelection_IsDisabled()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentsPage> cut = RenderPage(ctx, handler,
            PageJson(1, 25, 1, Item(FirstId, "AAA", "tok-a")));

        IElement button = cut.FindAll("button")
            .Single(b => b.TextContent == "Delete selected");

        Assert.That(button.HasAttribute("disabled"), Is.True);
    }
}
