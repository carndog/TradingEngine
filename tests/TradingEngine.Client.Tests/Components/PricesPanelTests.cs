using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Components;
using TradingEngine.Client.Services;
using TradingEngine.Client.Tests.Services;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Tests.Components;

[TestFixture]
public sealed class PricesPanelTests
{
    private static readonly Guid InstrumentA = Guid.Parse("e1030000-0000-0000-0000-0000000000c1");
    private static readonly Guid InstrumentB = Guid.Parse("e1030000-0000-0000-0000-0000000000c2");
    private static readonly DateTimeOffset Slot = DateTimeOffset.Parse("2026-10-30T13:45:00Z");

    [Test]
    public void Render_GbxHistoryWithDefaults_ShowsPoundsAtTwoPlacesAndMinuteTimes()
    {
        StubHttpMessageHandler handler = new();
        PriceObservationDto newest = Observation(Slot.AddTicks(1234567), 1234.56789012345679m, null, "GBX")
            with { Timeliness = "delayed" };
        PriceObservationDto older = Observation(Slot.AddMinutes(-1), 1234.5m, 1250m, "GBX");
        handler.Enqueue(Page(InstrumentA, newest, [newest, older], null));
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("[data-testid=latest-bid]").TextContent.Trim(), Is.EqualTo("12.35"));
            Assert.That(cut.Find("[data-testid=latest-ask]").TextContent.Trim(), Is.EqualTo(PriceObservationFormat.Missing));
            Assert.That(cut.Markup, Does.Contain("Bid (GBP)"));
            Assert.That(cut.Markup, Does.Contain("Ask (GBP)"));
            Assert.That(cut.Find("[data-testid=latest-meta]").TextContent, Does.Contain("30 Oct 2026, 13:45"));
            Assert.That(cut.Find("section").TextContent, Does.Not.Contain("13:45:00"));
            Assert.That(cut.Markup, Does.Not.Contain("UTC</span>"));
            Assert.That(cut.Markup, Does.Not.Contain("GBX"));
            Assert.That(cut.FindAll("tbody td")[1].TextContent.Trim(), Is.EqualTo("12.35"));
            Assert.That(cut.FindAll("tbody td")[6].TextContent.Trim(), Is.EqualTo("12.50"));
            Assert.That(cut.FindAll("th[scope=col]").Select(header => header.TextContent.Trim()),
                Is.EqualTo(new[] { "Scheduled (UTC)", "Bid (GBP)", "Ask (GBP)", "Status" }));
            Assert.That(cut.Find("[data-testid=price-range]").TextContent, Is.EqualTo("Page 1 · 2 observations"));
            Assert.That(cut.Find("button[aria-label='Older saved observations']").HasAttribute("disabled"), Is.True);
        });
    }

    [Test]
    public void Render_NativeGbxPreference_ShowsPenceWithThousandsSeparator()
    {
        StubHttpMessageHandler handler = new();
        PriceObservationDto newest = Observation(Slot, 1234.56789012345679m, 1234.565m, "GBX");
        handler.Enqueue(Page(InstrumentA, newest, [newest], null));
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA, "GBX", 2, PriceObservationFormat.NativeDisplayUnit);

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("[data-testid=latest-bid]").TextContent.Trim(), Is.EqualTo("1,234.57"));
            Assert.That(cut.Find("[data-testid=latest-ask]").TextContent.Trim(), Is.EqualTo("1,234.57"));
            Assert.That(cut.Markup, Does.Contain("Bid (GBX)"));
        });
    }

    [Test]
    public void Render_ZeroDecimalPlaces_ShowsWholeUnits()
    {
        StubHttpMessageHandler handler = new();
        PriceObservationDto newest = Observation(Slot, 68000.5m, 68010.49m, "USD");
        handler.Enqueue(Page(InstrumentA, newest, [newest], null));
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA, "USD", 0, PriceObservationFormat.MajorDisplayUnit);

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("[data-testid=latest-bid]").TextContent.Trim(), Is.EqualTo("68,001"));
            Assert.That(cut.Find("[data-testid=latest-ask]").TextContent.Trim(), Is.EqualTo("68,010"));
            Assert.That(cut.Markup, Does.Contain("Bid (USD)"));
        });
    }

    [Test]
    public void Render_RowInDifferentStoredUnit_ShowsUnitColumnAndFlagsRow()
    {
        StubHttpMessageHandler handler = new();
        PriceObservationDto newest = Observation(Slot, 1200m, 1210m, "GBX");
        PriceObservationDto older = Observation(Slot.AddMinutes(-1), 15.5m, 15.75m, "USD");
        handler.Enqueue(Page(InstrumentA, newest, [newest, older], null));
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("th[scope=col]").Select(header => header.TextContent.Trim()), Does.Contain("Unit"));
            Assert.That(cut.FindAll("tr.te-other-unit"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("tr.te-other-unit").TextContent, Does.Contain("USD"));
            Assert.That(cut.Find("tr.te-other-unit").TextContent, Does.Contain("15.50"));
        });
    }

    [Test]
    public void ShowDetails_Clicked_RevealsProvenanceColumnsWithDashForMissingProviderTime()
    {
        StubHttpMessageHandler handler = new();
        PriceObservationDto newest = Observation(Slot, 1200m, 1210m, "GBX")
            with { ProviderObservedAt = Slot.AddSeconds(28).AddTicks(1) };
        PriceObservationDto older = Observation(Slot.AddMinutes(-1), 1200m, 1210m, "GBX");
        handler.Enqueue(Page(InstrumentA, newest, [newest, older], null));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));

        cut.Find("button[aria-controls=te-price-history]").Click();

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("button[aria-controls=te-price-history]").GetAttribute("aria-expanded"), Is.EqualTo("true"));
            Assert.That(cut.Find("button[aria-controls=te-price-history]").TextContent.Trim(), Is.EqualTo("Hide details"));
            Assert.That(cut.FindAll("th[scope=col]").Select(header => header.TextContent.Trim()),
                Is.EqualTo(new[] { "Scheduled (UTC)", "Bid (GBP)", "Ask (GBP)", "Status", "Retrieved (UTC)", "Provider observed (UTC)", "Provider" }));
            Assert.That(cut.FindAll("tbody tr")[0].TextContent, Does.Contain("30 Oct 2026, 13:45"));
            Assert.That(cut.FindAll("tbody tr")[0].TextContent, Does.Not.Contain("13:45:28"));
            Assert.That(cut.FindAll("tbody tr")[1].Children[5].TextContent.Trim(), Is.EqualTo(PriceObservationFormat.Missing));
            Assert.That(cut.FindAll("tbody tr")[1].TextContent, Does.Contain("synthetic"));
        });
    }

    [Test]
    public void ChangePageSize_FromOlderPage_ReloadsFirstPageWithNewSize()
    {
        StubHttpMessageHandler handler = new();
        List<string> requests = new();
        DateTimeOffset cursor = Slot.AddMinutes(-9).AddTicks(7);
        PriceObservationDto newest = Observation(Slot, 1m, 2m, "GBX");
        EnqueueCapturing(handler, requests, Page(InstrumentA, newest, [newest], cursor));
        EnqueueCapturing(handler, requests, Page(InstrumentA, newest, [Observation(cursor.AddMinutes(-1), 1m, 2m, "GBX")], null));
        EnqueueCapturing(handler, requests, Page(InstrumentA, newest, [newest], null));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);
        cut.WaitForAssertion(() => Assert.That(requests, Has.Count.EqualTo(1)));
        cut.Find("button[aria-label='Older saved observations']").Click();
        cut.WaitForAssertion(() => Assert.That(requests, Has.Count.EqualTo(2)));

        cut.Find("select[aria-label='Rows per page']").Change("25");

        cut.WaitForAssertion(() => Assert.That(requests, Has.Count.EqualTo(3)));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0], Does.Contain("pageSize=10"));
            Assert.That(requests[1], Does.Contain("before=2026-10-30T13%3A36%3A00.0000007Z"));
            Assert.That(requests[2], Does.EndWith("price-observations?pageSize=25"));
            Assert.That(cut.Find("[data-testid=price-range]").TextContent, Does.StartWith("Page 1"));
        });
    }

    [Test]
    public void Render_EqualPricesInTwoSlots_ShowsBothRows()
    {
        StubHttpMessageHandler handler = new();
        PriceObservationDto newer = Observation(Slot, 10.5m, 10.75m, "USD");
        PriceObservationDto older = Observation(Slot.AddMinutes(-1), 10.5m, 10.75m, "USD");
        handler.Enqueue(Page(InstrumentA, newer, [newer, older], null));
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(2)));
    }

    [Test]
    public void Render_NoObservations_ShowsEmptyState()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(Page(InstrumentA, null, [], null));
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);

        cut.WaitForAssertion(() => Assert.That(
            cut.Markup,
            Does.Contain("No price observations have been saved for this instrument yet.")));
    }

    [Test]
    public void Render_WhileLoading_ShowsLoadingState()
    {
        StubHttpMessageHandler handler = new();
        TaskCompletionSource<HttpResponseMessage> pending = new();
        handler.Enqueue((_, _) => pending.Task);
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Loading saved prices…"));
            Assert.That(cut.Find("[role=status]").TextContent, Does.Contain("Loading saved prices…"));
        });
    }

    [Test]
    public void Retry_AfterServerError_LoadsHistory()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        PriceObservationDto row = Observation(Slot, 1m, 2m, "USD");
        handler.Enqueue(Page(InstrumentA, row, [row], null));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[role=alert]"), Has.Count.EqualTo(1)));

        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("tbody tr"), Has.Count.EqualTo(1)));
        Assert.That(cut.FindAll("[role=alert]"), Is.Empty);
    }

    [Test]
    public void Render_Unauthorized_ShowsSignInLink()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);

        cut.WaitForAssertion(() => Assert.That(
            cut.Find("[role=alert] a").TextContent,
            Is.EqualTo("Sign in again")));
    }

    [Test]
    public void Paging_OlderNewerAndRefresh_SendsExpectedCursors()
    {
        StubHttpMessageHandler handler = new();
        List<string> requests = [];
        DateTimeOffset cursor = Slot.AddTicks(1234567);
        PriceObservationDto latest = Observation(Slot.AddMinutes(5), 9m, 9m, "USD");
        PriceObservationDto older = Observation(Slot, 1m, 1m, "USD");
        EnqueueCapturing(handler, requests, Page(InstrumentA, latest, [latest], cursor));
        EnqueueCapturing(handler, requests, Page(InstrumentA, latest, [older], null));
        EnqueueCapturing(handler, requests, Page(InstrumentA, latest, [latest], cursor));
        EnqueueCapturing(handler, requests, Page(InstrumentA, latest, [older], null));
        EnqueueCapturing(handler, requests, Page(InstrumentA, latest, [latest], cursor));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);
        cut.WaitForAssertion(() => Assert.That(requests, Has.Count.EqualTo(1)));

        cut.WaitForAssertion(() => cut.Find("button[aria-label='Older saved observations']").Click());
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Page 2")));
        cut.Find("button[aria-label='Newer saved observations']").Click();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Page 1")));
        cut.WaitForAssertion(() => cut.Find("button[aria-label='Older saved observations']").Click());
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Page 2")));
        cut.FindAll("button").First(button => button.TextContent.Trim() == "Refresh").Click();
        cut.WaitForAssertion(() => Assert.That(requests, Has.Count.EqualTo(5)));

        Assert.Multiple(() =>
        {
            Assert.That(requests[0], Is.EqualTo($"/api/watched-instruments/{InstrumentA}/price-observations?pageSize=10"));
            Assert.That(requests[1], Does.EndWith("pageSize=10&before=2026-10-30T13%3A45%3A00.1234567Z"));
            Assert.That(requests[2], Does.Not.Contain("before="));
            Assert.That(requests[3], Does.Contain("before="));
            Assert.That(requests[4], Does.Not.Contain("before="));
            Assert.That(cut.Markup, Does.Contain("Page 1"));
        });
    }

    [Test]
    public async Task InstrumentChange_PreviousResponseInFlight_DoesNotReplaceNewInstrumentHistory()
    {
        StubHttpMessageHandler handler = new();
        TaskCompletionSource<HttpResponseMessage> delayedA = new();
        CancellationToken capturedA = CancellationToken.None;
        handler.Enqueue((_, token) =>
        {
            capturedA = token;
            return delayedA.Task;
        });
        PriceObservationDto rowB = Observation(Slot, 222m, 223m, "EUR");
        handler.Enqueue(Page(InstrumentB, rowB, [rowB], null));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<PricesPanel> cut = RenderPanel(ctx, InstrumentA);

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(PricesPanel.InstrumentId)] = InstrumentB })));
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("222")));
        PriceObservationDto rowA = Observation(Slot, 111m, 112m, "USD");
        delayedA.SetResult(Page(InstrumentA, rowA, [rowA], null));
        await Task.Delay(100);

        Assert.Multiple(() =>
        {
            Assert.That(capturedA.IsCancellationRequested, Is.True);
            Assert.That(cut.Markup, Does.Contain("222"));
            Assert.That(cut.Markup, Does.Not.Contain("111"));
        });
    }

    [Test]
    public void Dispose_WithRequestInFlight_CancelsRequest()
    {
        StubHttpMessageHandler handler = new();
        CancellationToken captured = CancellationToken.None;
        handler.Enqueue((_, token) =>
        {
            captured = token;
            return new TaskCompletionSource<HttpResponseMessage>().Task;
        });
        BunitContext ctx = CreateContext(handler);
        RenderPanel(ctx, InstrumentA);

        ctx.Dispose();

        Assert.That(captured.IsCancellationRequested, Is.True);
    }

    [TestCase(null, 2, "—")]
    [TestCase("1.2", 2, "1.20")]
    [TestCase("1.2", 0, "1")]
    [TestCase("2.345", 2, "2.35")]
    [TestCase("-2.345", 2, "-2.35")]
    [TestCase("2.5", 0, "3")]
    [TestCase("68000.5", 0, "68,001")]
    [TestCase("0.525", 2, "0.53")]
    [TestCase("1234567.891", 2, "1,234,567.89")]
    [TestCase("0.123456789", 8, "0.12345679")]
    public void FormatPrice_DecimalPlaces_RoundsAwayFromZeroWithFixedPlaces(string? stored, int places, string expected)
    {
        decimal? value = stored is null ? null : decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture);

        string formatted = PriceObservationFormat.FormatPrice(value, places);

        Assert.That(formatted, Is.EqualTo(expected));
    }

    [TestCase("1234.56789012345679", "GBP", "12.35")]
    [TestCase("1234.56789012345679", "GBX", "1,234.57")]
    [TestCase("0.5", "GBP", "0.01")]
    [TestCase("0.49", "GBP", "0.00")]
    public void FormatPrice_GbxStoredValue_ConvertsBeforeRounding(string stored, string displayUnit, string expected)
    {
        decimal value = decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture);

        string formatted = PriceObservationFormat.FormatPrice(value, "GBX", displayUnit, 2);

        Assert.That(formatted, Is.EqualTo(expected));
    }

    [TestCase("GBX", "major", "GBP")]
    [TestCase("GBX", "native", "GBX")]
    [TestCase("USD", "major", "USD")]
    [TestCase("GBP", "native", "GBP")]
    public void DisplayUnit_StoredUnitAndPreference_ReturnsDisplayedUnit(string stored, string preference, string expected)
    {
        string unit = PriceObservationFormat.DisplayUnit(stored, preference);

        Assert.That(unit, Is.EqualTo(expected));
    }

    [Test]
    public void ToDisplayUnit_GbxToGbp_KeepsFullDecimalPrecision()
    {
        decimal? converted = PriceObservationFormat.ToDisplayUnit(1234.56789012345679m, "GBX", "GBP");

        Assert.That(converted, Is.EqualTo(12.3456789012345679m));
    }

    [Test]
    public void FormatMinute_PreciseInstant_ShowsDateHoursAndMinutesOnly()
    {
        string formatted = PriceObservationFormat.FormatMinute(DateTimeOffset.Parse("2026-11-06T10:25:59.9999999Z"));

        Assert.That(formatted, Is.EqualTo("06 Nov 2026, 10:25"));
    }

    private static BunitContext CreateContext(StubHttpMessageHandler handler)
    {
        BunitContext ctx = new();
        HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost/") };
        ctx.Services.AddSingleton(new TradingEngineApiClient(http, TimeSpan.FromSeconds(30), []));
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static IRenderedComponent<PricesPanel> RenderPanel(BunitContext ctx, Guid instrumentId)
    {
        return RenderPanel(ctx, instrumentId, "GBX", 2, PriceObservationFormat.MajorDisplayUnit);
    }

    private static IRenderedComponent<PricesPanel> RenderPanel(
        BunitContext ctx,
        Guid instrumentId,
        string quoteCurrency,
        int decimalPlaces,
        string displayUnit)
    {
        return ctx.Render<PricesPanel>(parameters => parameters
            .Add(component => component.InstrumentId, instrumentId)
            .Add(component => component.QuoteCurrency, quoteCurrency)
            .Add(component => component.DecimalPlaces, decimalPlaces)
            .Add(component => component.DisplayUnitPreference, displayUnit)
            .Add(component => component.SignInUrl, "/.auth/login/aad"));
    }

    private static void EnqueueCapturing(
        StubHttpMessageHandler handler,
        List<string> requests,
        HttpResponseMessage response)
    {
        handler.Enqueue((request, _) =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(response);
        });
    }

    private static PriceObservationDto Observation(DateTimeOffset slot, decimal? bid, decimal? ask, string unit)
    {
        return new PriceObservationDto(slot, slot.AddSeconds(2), null, bid, ask, unit, "synthetic", "realtime");
    }

    private static HttpResponseMessage Page(
        Guid instrumentId,
        PriceObservationDto? latest,
        IReadOnlyList<PriceObservationDto> items,
        DateTimeOffset? nextBefore)
    {
        return StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(new PriceObservationPageResponse(instrumentId, latest, items, 25, null, nextBefore)));
    }
}
