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
        WatchedInstrumentSettingsResponse settings)
    {
        return ctx.Render<InstrumentSettingsEditor>(
            parameters => parameters
                .Add(component => component.Settings, settings)
                .Add(component => component.SignInUrl, "signin.html")
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
