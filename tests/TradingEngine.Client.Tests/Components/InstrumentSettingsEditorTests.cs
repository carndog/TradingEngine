using System.Net;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Components;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Tests.Components;

[TestFixture]
public sealed class InstrumentSettingsEditorTests
{
    private static readonly Guid InstrumentId = Guid.Parse("d5000000-0000-0000-0000-000000000001");

    [Test]
    public void Save_DisplayPreferencesChanged_SendsDecimalPlacesAndUnit()
    {
        StubHttpMessageHandler handler = new();
        List<string> bodies = [];
        WatchedInstrumentSettingsResponse settings = ClientTestData.Settings(InstrumentId) with { QuoteCurrency = "GBX" };
        handler.Enqueue(async (request, _) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ClientTestData.JsonOf(settings with { PriceDisplayDecimalPlaces = 0, PriceDisplayUnit = "native", ConcurrencyToken = "tok-2" }));
        });
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(ctx, settings);

        cut.Find("input[aria-label='Price decimal places']").Input("0");
        cut.Find("select[aria-label='Display unit']").Input("native");
        ClickButton(cut, "Save settings");

        cut.WaitForAssertion(() => Assert.That(bodies, Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(bodies[0], Does.Contain("\"priceDisplayDecimalPlaces\":0"));
            Assert.That(bodies[0], Does.Contain("\"priceDisplayUnit\":\"native\""));
            Assert.That(cut.Markup, Does.Not.Contain("Unsaved changes"));
        });
    }

    [Test]
    public void Render_NonSubunitCurrency_HidesDisplayUnitChoice()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(ctx, ClientTestData.Settings(InstrumentId));

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("select[aria-label='Display unit']"), Is.Empty);
            Assert.That(cut.Find("input[aria-label='Price decimal places']").GetAttribute("value"), Is.EqualTo("2"));
        });
    }

    [Test]
    public void Render_GbxCurrency_OffersPoundsAndNativePence()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with { QuoteCurrency = "GBX" });

        Assert.That(
            cut.FindAll("select[aria-label='Display unit'] option").Select(option => option.TextContent.Trim()),
            Is.EqualTo(new[] { "GBP", "GBX (native)" }));
    }

    [Test]
    public void Render_WithoutListing_HidesListingSection()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId));

        Assert.That(cut.FindAll("section[aria-label='Listing configuration']"), Is.Empty);
    }

    [Test]
    public void Render_WithUnconfirmedListing_ShowsConfirmForm()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing()
            });

        Assert.Multiple(() =>
        {
            Assert.That(
                cut.FindAll("section[aria-label='Listing configuration']"),
                Has.Count.EqualTo(1));
            Assert.That(
                cut.FindAll("input[aria-label='Native quote unit to confirm']"),
                Has.Count.EqualTo(1));
            Assert.That(cut.Markup, Does.Contain("eToro"));
            Assert.That(cut.Markup, Does.Contain("Not confirmed"));
        });
    }

    [Test]
    public void Render_WithConfirmedListing_HidesConfirmForm()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing(
                    unitState: "confirmed",
                    unitSource: "administrator")
            });

        Assert.Multiple(() =>
        {
            Assert.That(
                cut.FindAll("input[aria-label='Native quote unit to confirm']"),
                Is.Empty);
            Assert.That(cut.Markup, Does.Contain("GBX"));
            Assert.That(cut.Markup, Does.Contain("confirmed"));
        });
    }

    [Test]
    public void Render_WithConflictListing_ShowsConflictNotice()
    {
        StubHttpMessageHandler handler = new();
        using BunitContext ctx = CreateContext(handler);

        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing(unitState: "conflict")
            });

        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("conflicts with the"));
            Assert.That(
                cut.FindAll("input[aria-label='Native quote unit to confirm']"),
                Is.Empty);
        });
    }

    [Test]
    public void ConfirmUnit_WhenClicked_SendsListingPutThenReloadsSettings()
    {
        StubHttpMessageHandler handler = new();
        List<(string Path, string Body, string? IfMatch)> requests = [];
        handler.Enqueue(async (request, _) =>
        {
            requests.Add((
                request.RequestUri!.AbsolutePath,
                await request.Content!.ReadAsStringAsync(),
                request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : null));
            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ClientTestData.JsonOf(ClientTestData.Listing(
                    unitState: "confirmed",
                    unitSource: "administrator")));
        });
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing(
                    unitState: "confirmed",
                    unitSource: "administrator")
            })));
        using BunitContext ctx = CreateContext(handler);
        string? notice = null;
        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing()
            },
            value => notice = value);

        cut.Find("input[aria-label='Native quote unit to confirm']").Input("gbx");
        ClickButton(cut, "Confirm unit");

        cut.WaitForAssertion(
            () => Assert.That(requests, Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(
                requests[0].Path,
                Is.EqualTo($"/api/watched-instruments/{InstrumentId}/listing"));
            Assert.That(requests[0].Body, Does.Contain("\"nativeQuoteCurrency\":\"gbx\""));
            Assert.That(requests[0].IfMatch, Is.EqualTo("\"listing-tok-1\""));
            Assert.That(notice, Is.EqualTo("Listing unit confirmed."));
            Assert.That(
                cut.FindAll("input[aria-label='Native quote unit to confirm']"),
                Is.Empty);
        });
    }

    [Test]
    public void ConfirmUnit_WhenSettingsEdited_PreservesUnsavedChanges()
    {
        StubHttpMessageHandler handler = new();
        List<(string Path, string Body, string? IfMatch)> requests = [];
        handler.Enqueue(async (request, _) =>
        {
            requests.Add((
                request.RequestUri!.AbsolutePath,
                await request.Content!.ReadAsStringAsync(),
                request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : null));
            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ClientTestData.JsonOf(ClientTestData.Listing(
                    unitState: "confirmed",
                    unitSource: "administrator")));
        });
        handler.Enqueue(async (request, _) =>
        {
            requests.Add((
                request.RequestUri!.AbsolutePath,
                await request.Content!.ReadAsStringAsync(),
                request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : null));
            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ClientTestData.JsonOf(ClientTestData.Settings(
                    InstrumentId,
                    interval: 120,
                    token: "tok-3") with
                {
                    Listing = ClientTestData.Listing(
                        unitState: "confirmed",
                        unitSource: "administrator")
                }));
        });
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing()
            });

        IntervalInput(cut).Input("120");
        cut.Find("input[aria-label='Native quote unit to confirm']").Input("gbx");
        ClickButton(cut, "Confirm unit");

        cut.WaitForAssertion(
            () => Assert.That(requests, Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(IntervalInput(cut).GetAttribute("value"), Is.EqualTo("120"));
            Assert.That(cut.Markup, Does.Contain("Unsaved changes"));
            Assert.That(cut.Markup, Does.Contain("confirmed"));
        });

        ClickButton(cut, "Save settings");

        cut.WaitForAssertion(
            () => Assert.That(requests, Has.Count.EqualTo(2)));
        Assert.Multiple(() =>
        {
            Assert.That(
                requests[1].Path,
                Is.EqualTo($"/api/watched-instruments/{InstrumentId}/settings"));
            Assert.That(
                requests[1].Body,
                Does.Contain("\"samplingIntervalSeconds\":120"));
            Assert.That(requests[1].IfMatch, Is.EqualTo("\"tok-1\""));
            Assert.That(cut.Markup, Does.Not.Contain("Unsaved changes"));
        });
    }

    [Test]
    public void ConfirmUnit_WhenChangedElsewhere_SaveConflictsWithOriginalToken()
    {
        StubHttpMessageHandler handler = new();
        List<(string Path, string? IfMatch)> requests = [];
        handler.Enqueue(async (request, _) =>
        {
            requests.Add((
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : null));
            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ClientTestData.JsonOf(ClientTestData.Listing(
                    unitState: "confirmed",
                    unitSource: "administrator")));
        });
        handler.Enqueue(async (request, _) =>
        {
            requests.Add((
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : null));
            return await Task.FromResult(
                StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed, "{}"));
        });
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(
                InstrumentId,
                token: "tok-2") with
            {
                PriceDisplayDecimalPlaces = 3,
                Listing = ClientTestData.Listing(
                    unitState: "confirmed",
                    unitSource: "administrator")
            })));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing()
            });

        IntervalInput(cut).Input("120");
        cut.Find("input[aria-label='Native quote unit to confirm']").Input("gbx");
        ClickButton(cut, "Confirm unit");

        cut.WaitForAssertion(
            () => Assert.That(requests, Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(IntervalInput(cut).GetAttribute("value"), Is.EqualTo("120"));
            Assert.That(cut.Markup, Does.Contain("Unsaved changes"));
            Assert.That(cut.Markup, Does.Contain("confirmed"));
        });

        ClickButton(cut, "Save settings");

        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Changed elsewhere")));
        Assert.Multiple(() =>
        {
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].IfMatch, Is.EqualTo("\"tok-1\""));
            Assert.That(cut.Markup, Does.Contain("60 s"));
            Assert.That(cut.Markup, Does.Contain("3 dp"));
            Assert.That(cut.Markup, Does.Contain("120 s"));
        });
    }

    [Test]
    public void ConflictReapply_AfterConfirm_SavesEditsAgainstLatestVersion()
    {
        StubHttpMessageHandler handler = new();
        List<(string Path, string? IfMatch, string Body)> requests = [];
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Listing(
                unitState: "confirmed",
                unitSource: "administrator"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed, "{}"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(
                InstrumentId,
                token: "tok-2") with
            {
                PriceDisplayDecimalPlaces = 3,
                Listing = ClientTestData.Listing(
                    unitState: "confirmed",
                    unitSource: "administrator")
            })));
        handler.Enqueue(async (request, _) =>
        {
            requests.Add((
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : null,
                await request.Content!.ReadAsStringAsync()));
            return StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ClientTestData.JsonOf(ClientTestData.Settings(
                    InstrumentId,
                    interval: 120,
                    token: "tok-3") with
                {
                    PriceDisplayDecimalPlaces = 3,
                    Listing = ClientTestData.Listing(
                        unitState: "confirmed",
                        unitSource: "administrator")
                }));
        });
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing()
            });

        IntervalInput(cut).Input("120");
        ClickButton(cut, "Confirm unit");
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("confirmed")));
        ClickButton(cut, "Save settings");
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Changed elsewhere")));

        ClickButton(cut, "Keep my input and retry");

        cut.WaitForAssertion(
            () => Assert.That(requests, Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].IfMatch, Is.EqualTo("\"tok-2\""));
            Assert.That(
                requests[0].Body,
                Does.Contain("\"samplingIntervalSeconds\":120"));
            Assert.That(
                cut.Find("input[aria-label='Price decimal places']").GetAttribute("value"),
                Is.EqualTo("3"));
            Assert.That(cut.Markup, Does.Not.Contain("Unsaved changes"));
        });
    }

    [Test]
    public void ConflictDiscard_AfterConfirm_AdoptsLatestSettingsAndListing()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Listing(
                unitState: "confirmed",
                unitSource: "administrator"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed, "{}"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(
                InstrumentId,
                interval: 30,
                token: "tok-2") with
            {
                PriceDisplayDecimalPlaces = 3,
                Listing = ClientTestData.Listing(
                    unitState: "confirmed",
                    unitSource: "administrator")
            })));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing()
            });

        IntervalInput(cut).Input("120");
        ClickButton(cut, "Confirm unit");
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("confirmed")));
        ClickButton(cut, "Save settings");
        cut.WaitForAssertion(
            () => Assert.That(cut.Markup, Does.Contain("Changed elsewhere")));

        ClickButton(cut, "Use latest and discard my input");

        cut.WaitForAssertion(
            () => Assert.That(IntervalInput(cut).GetAttribute("value"), Is.EqualTo("30")));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Unsaved changes"));
            Assert.That(cut.Markup, Does.Not.Contain("Changed elsewhere"));
            Assert.That(cut.Markup, Does.Contain("confirmed"));
        });
    }

    [Test]
    public void ConfirmUnit_WhenRequestFails_ShowsError()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.Conflict,
            """{"title":"Conflict","status":409,"code":"market_data.quote_currency_conflict","detail":"The requested unit conflicts with the provider listing."}"""));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(
            ctx,
            ClientTestData.Settings(InstrumentId) with
            {
                Listing = ClientTestData.Listing()
            });

        cut.Find("input[aria-label='Native quote unit to confirm']").Input("USD");
        ClickButton(cut, "Confirm unit");

        cut.WaitForAssertion(
            () => Assert.That(
                cut.Markup,
                Does.Contain("conflicts with the provider listing")));
    }

    private static BunitContext CreateContext(StubHttpMessageHandler handler)
    {
        BunitContext ctx = new();
        HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost/") };
        ctx.Services.AddSingleton(new TradingEngineApiClient(http, TimeSpan.FromSeconds(30), []));
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static IRenderedComponent<InstrumentSettingsEditor> RenderEditor(
        BunitContext ctx,
        WatchedInstrumentSettingsResponse settings,
        Action<string>? onNotice = null)
    {
        return ctx.Render<InstrumentSettingsEditor>(
            parameters => parameters
                .Add(component => component.Settings, settings)
                .Add(component => component.SignInUrl, "signin.html")
                .Add(
                    component => component.OnNotice,
                    onNotice is null
                        ? default
                        : EventCallback.Factory.Create<string>(
                            new object(),
                            onNotice))
                .Add(component => component.Cancellation, CancellationToken.None));
    }

    private static IElement IntervalInput(IRenderedComponent<InstrumentSettingsEditor> cut)
    {
        return cut.Find("input[type=number]");
    }

    private static void ClickButton(IRenderedComponent<InstrumentSettingsEditor> cut, string text)
    {
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains(text))
            .Click();
    }

    [Test]
    public void SettingsUpdate_WhileDirty_PreservesUserInput()
    {
        using BunitContext ctx = CreateContext(new StubHttpMessageHandler());
        WatchedInstrumentSettingsResponse original = ClientTestData.Settings(InstrumentId);
        IRenderedComponent<InstrumentSettingsEditor> cut = RenderEditor(ctx, original);
        IntervalInput(cut).Input("120");

        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InstrumentSettingsEditor.Settings)] =
                    ClientTestData.Settings(InstrumentId, interval: 30, token: "tok-2")
            })));

        Assert.Multiple(() =>
        {
            Assert.That(IntervalInput(cut).GetAttribute("value"), Is.EqualTo("120"));
            Assert.That(cut.Markup, Does.Contain("Unsaved changes"));
        });
    }

    [Test]
    public void SettingsUpdate_WhileClean_AdoptsLatestValues()
    {
        using BunitContext ctx = CreateContext(new StubHttpMessageHandler());
        IRenderedComponent<InstrumentSettingsEditor> cut =
            RenderEditor(ctx, ClientTestData.Settings(InstrumentId));

        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InstrumentSettingsEditor.Settings)] =
                    ClientTestData.Settings(InstrumentId, interval: 30, token: "tok-2")
            })));

        Assert.That(IntervalInput(cut).GetAttribute("value"), Is.EqualTo("30"));
    }

    [Test]
    public void Save_PreconditionFailed_ShowsConflictAndKeepsInput()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed, "{}"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, interval: 30, token: "tok-2"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut =
            RenderEditor(ctx, ClientTestData.Settings(InstrumentId));
        IntervalInput(cut).Input("120");

        ClickButton(cut, "Save settings");

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Changed elsewhere")));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("30 s"));
            Assert.That(IntervalInput(cut).GetAttribute("value"), Is.EqualTo("120"));
        });
    }

    [Test]
    public void Save_PreconditionFailedTwice_ReopensConflictSafely()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed, "{}"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, interval: 30, token: "tok-2"))));
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed, "{}"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, interval: 45, token: "tok-3"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut =
            RenderEditor(ctx, ClientTestData.Settings(InstrumentId));
        IntervalInput(cut).Input("120");
        ClickButton(cut, "Save settings");
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Changed elsewhere")));

        ClickButton(cut, "Keep my input and retry");

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("45 s")));
        Assert.That(IntervalInput(cut).GetAttribute("value"), Is.EqualTo("120"));
    }

    [Test]
    public void ConflictReapply_SecondSave_UsesFreshToken()
    {
        StubHttpMessageHandler handler = new();
        List<string?> ifMatchValues = [];
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed, "{}"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, interval: 30, token: "tok-2"))));
        handler.Enqueue((request, _) =>
        {
            ifMatchValues.Add(request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                ? string.Join(",", values)
                : null);
            return Task.FromResult(StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, interval: 120, token: "tok-3"))));
        });
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut =
            RenderEditor(ctx, ClientTestData.Settings(InstrumentId));
        IntervalInput(cut).Input("120");
        ClickButton(cut, "Save settings");
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Changed elsewhere")));

        ClickButton(cut, "Keep my input and retry");

        cut.WaitForAssertion(() => Assert.That(ifMatchValues, Has.Count.EqualTo(1)));
        Assert.That(ifMatchValues[0], Is.EqualTo("\"tok-2\""));
    }

    [Test]
    public void ConflictDiscard_ReseedsInputsFromLatest()
    {
        StubHttpMessageHandler handler = new();
        handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.PreconditionFailed, "{}"));
        handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            ClientTestData.JsonOf(ClientTestData.Settings(InstrumentId, interval: 30, token: "tok-2"))));
        using BunitContext ctx = CreateContext(handler);
        IRenderedComponent<InstrumentSettingsEditor> cut =
            RenderEditor(ctx, ClientTestData.Settings(InstrumentId));
        IntervalInput(cut).Input("120");
        ClickButton(cut, "Save settings");
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Changed elsewhere")));

        ClickButton(cut, "Use latest and discard my input");

        Assert.Multiple(() =>
        {
            Assert.That(IntervalInput(cut).GetAttribute("value"), Is.EqualTo("30"));
            Assert.That(cut.Markup, Does.Not.Contain("Changed elsewhere"));
            Assert.That(cut.Markup, Does.Not.Contain("Unsaved changes"));
        });
    }
}
