using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Pages;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Tests.Pages;

[TestFixture]
public sealed class InstrumentCreatePageTests
{
    [Test]
    public async Task RegisterInstrument_ResistanceEntryOptIn_SubmitsExplicitCondition()
    {
        Guid instrumentId = Guid.Parse("d5000000-0000-0000-0000-000000000002");
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        RegisterWatchedInstrumentRequest? submitted = null;
        StubHttpMessageHandler handler = new();
        handler.Enqueue(async (request, cancellationToken) =>
        {
            submitted = await request.Content!.ReadFromJsonAsync<RegisterWatchedInstrumentRequest>(cancellationToken);
            WatchedInstrumentResponse response = new(instrumentId, "SYNTHETIC", "XTEST", "USD", "configured", 60,
                createdAt, createdAt, submitted!.PriceScale, submitted.SupportZones!, submitted.ResistanceZones!);
            return StubHttpMessageHandler.Json(HttpStatusCode.Created, ClientTestData.JsonOf(response));
        });
        using BunitContext ctx = new();
        ctx.Services.AddSingleton(new TradingEngineApiClient(new HttpClient(handler)
            { BaseAddress = new Uri("http://localhost/") }, TimeSpan.FromSeconds(30), []));
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        IRenderedComponent<InstrumentCreatePage> cut = ctx.Render<InstrumentCreatePage>();
        cut.Find("input[aria-label='Symbol']").Input("SYNTHETIC");
        cut.Find("input[aria-label='Exchange']").Input("XTEST");
        cut.Find("input[aria-label='Quote currency']").Input("USD");
        cut.FindAll("button").Single(button => button.TextContent == "Remove zone").Click();
        cut.FindAll("button").Single(button => button.TextContent.Contains("Add resistance", StringComparison.Ordinal)).Click();
        cut.Find("input[aria-label='Zone identifier']").Input("resistance-a");
        cut.Find("input[aria-label='Lower bound']").Input("120");
        cut.Find("input[aria-label='Level']").Input("125");
        cut.Find("input[aria-label='Upper bound']").Input("130");
        cut.Find("input[aria-label='breakout action identifier']").Input("publish-signal");
        cut.Find("input[aria-label='Enable resistance entry alert']").Change(true);

        await cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Register instrument").Click());

        cut.WaitForAssertion(() => Assert.That(submitted, Is.Not.Null));
        Assert.That(submitted!.ResistanceZones![0].Conditions!.Select(condition => condition.Type),
            Is.EqualTo(new[] { "breakout", "resistance-entry" }));
        Assert.That(submitted.ResistanceZones[0].Conditions![1].ActionId, Is.EqualTo("publish-signal"));
        Assert.That(ctx.Services.GetRequiredService<NavigationManager>().Uri,
            Does.EndWith($"instruments/{instrumentId}?tab=current"));
    }
}
