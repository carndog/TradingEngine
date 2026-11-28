using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Client.Components;
using TradingEngine.Client.Pages;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Tests.Components;

[TestFixture]
public sealed class InstrumentVerificationTests
{
    private BunitContext _context = null!;
    private StubHttpMessageHandler _http = null!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _http = new StubHttpMessageHandler();
        _context.Services.AddSingleton(new TradingEngineApiClient(new HttpClient(_http)
            { BaseAddress = new Uri("http://localhost/") }, TimeSpan.FromSeconds(30), []));
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public void VerifyAsync_MissingInputs_DisablesCheckAndExplainsInitialState()
    {
        IRenderedComponent<InstrumentVerification> cut = _context.Render<InstrumentVerification>();

        Assert.That(cut.Find("button").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("[role=status]").TextContent, Does.Contain("Not checked"));
        Assert.That(cut.Find("[role=status]").GetAttribute("aria-live"), Is.EqualTo("polite"));
    }

    [Test]
    public async Task VerifyAsync_RecognisedDelayedOneSidedQuote_ShowsNativeUnitsAndTimestamps()
    {
        VerifyInstrumentRequest? submitted = null;
        _http.Enqueue(async (request, token) =>
        {
            submitted = await request.Content!.ReadFromJsonAsync<VerifyInstrumentRequest>(token);
            return Json(Recognised());
        });
        IRenderedComponent<InstrumentVerification> cut = Render();

        await cut.Find("button").ClickAsync(new());

        Assert.That(submitted, Is.EqualTo(new VerifyInstrumentRequest("DEMO-2", "XTEST", "USD")));
        Assert.That(cut.Markup, Does.Contain("Recognised"));
        Assert.That(cut.Markup, Does.Contain("Synthetic Demo"));
        Assert.That(cut.Markup, Does.Contain("Test Exchange"));
        Assert.That(cut.Markup, Does.Contain("Bid: 12.345"));
        Assert.That(cut.Markup, Does.Contain("Ask: Unavailable"));
        Assert.That(cut.Markup, Does.Contain("USD"));
        Assert.That(cut.Markup, Does.Contain("2026-01-02 12:00:00 UTC"));
        Assert.That(cut.Markup, Does.Contain("delayed"));
        Assert.That(cut.Markup, Does.Contain("not a live stream"));
    }

    [TestCase("not-found", "market_data.instrument_mapping_unresolved", "Not found", "exact listing")]
    [TestCase("ambiguous", "market_data.instrument_mapping_ambiguous", "Multiple matches", "administrator")]
    [TestCase("unsupported-venue", "market_data.exchange_mapping_unconfigured", "Unable to check", "provider mapping")]
    [TestCase("unavailable", "market_data.provider_throttled", "Unable to check", "request limit")]
    public async Task VerifyAsync_ProviderCannotResolve_ShowsActionableState(
        string state, string code, string heading, string guidance)
    {
        _http.Enqueue(Json(Recognised() with { ResolutionState = state, ResolutionDetailCode = code, Quote = null }));
        IRenderedComponent<InstrumentVerification> cut = Render();

        await cut.Find("button").ClickAsync(new());

        Assert.That(cut.Markup, Does.Contain(heading));
        Assert.That(cut.Markup, Does.Contain(guidance));
    }

    [TestCase("symbol")]
    [TestCase("exchange")]
    [TestCase("currency")]
    [TestCase("native")]
    public async Task VerifyAsync_InputChanges_ClearsPreviouslyRecognisedResult(string input)
    {
        _http.Enqueue(Json(Recognised()));
        IRenderedComponent<InstrumentVerification> cut = Render();
        await cut.Find("button").ClickAsync(new());

        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(InstrumentVerification.Symbol)] = input == "symbol" ? "NEW-1" : "DEMO-2",
                [nameof(InstrumentVerification.Exchange)] = input == "exchange" ? "XNEW" : "XTEST",
                [nameof(InstrumentVerification.QuoteCurrency)] = input == "currency" ? "GBP" : "USD",
                [nameof(InstrumentVerification.NativeQuoteCurrency)] = input == "native" ? "GBP" : ""
            })));

        Assert.That(cut.Markup, Does.Contain("Not checked"));
        Assert.That(cut.Markup, Does.Not.Contain("Synthetic Demo"));
    }

    [Test]
    public async Task VerifyAsync_EditedInputsDuringSlowRequest_CancelsAndIgnoresLateResponse()
    {
        TaskCompletionSource<HttpResponseMessage> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        _http.Enqueue((_, token) =>
        {
            requestToken = token;
            return pending.Task;
        });
        _http.Enqueue(Json(Recognised() with { Symbol = "NEW-1", ProviderSymbol = "NEW-1", DisplayName = "New listing" }));
        IRenderedComponent<InstrumentVerification> cut = Render();

        Task first = cut.Find("button").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.That(cut.Find("button").HasAttribute("disabled"), Is.True));
        await cut.Find("button").ClickAsync(new());
        Assert.That(_http.RequestCount, Is.EqualTo(1));
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(InstrumentVerification.Symbol)] = "NEW-1" })));
        Assert.That(requestToken.IsCancellationRequested, Is.True);
        await cut.Find("button").ClickAsync(new());
        pending.SetResult(Json(Recognised()));
        await first;

        Assert.That(cut.Markup, Does.Contain("New listing"));
        Assert.That(cut.Markup, Does.Not.Contain("Synthetic Demo"));
        Assert.That(_http.RequestCount, Is.EqualTo(2));
    }

    [Test]
    public async Task VerifyAsync_DisposedComponent_CancelsRequest()
    {
        TaskCompletionSource<HttpResponseMessage> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        _http.Enqueue((_, token) =>
        {
            requestToken = token;
            return pending.Task;
        });
        IRenderedComponent<InstrumentVerification> cut = Render();

        Task request = cut.Find("button").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.That(_http.RequestCount, Is.EqualTo(1)));
        await _context.DisposeComponentsAsync();
        pending.SetResult(Json(Recognised()));
        await request;

        Assert.That(requestToken.IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task VerifyAsync_FailedCheckOnCreatePage_PreservesFormAndCanRetry()
    {
        _http.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "{\"detail\":\"synthetic-private-detail\"}"));
        _http.Enqueue(Json(Recognised()));
        IRenderedComponent<InstrumentCreatePage> cut = _context.Render<InstrumentCreatePage>();
        cut.Find("input[aria-label='Symbol']").Input("DEMO-2");
        cut.Find("input[aria-label='Exchange']").Input("XTEST");
        cut.Find("input[aria-label='Quote currency']").Input("USD");
        cut.Find("input[aria-label='Lower bound']").Input("10");
        cut.Find("input[aria-label='Level']").Input("11");
        cut.Find("input[aria-label='Upper bound']").Input("12");

        await cut.Find("section[aria-label='Instrument verification'] button").ClickAsync(new());

        Assert.That(cut.Markup, Does.Contain("Unable to check"));
        Assert.That(cut.Markup, Does.Not.Contain("synthetic-private-detail"));
        Assert.That(cut.Find("input[aria-label='Lower bound']").GetAttribute("value"), Is.EqualTo("10"));
        Assert.That(cut.Find("input[aria-label='Level']").GetAttribute("value"), Is.EqualTo("11"));
        Assert.That(cut.Find("input[aria-label='Upper bound']").GetAttribute("value"), Is.EqualTo("12"));
        await cut.Find("section[aria-label='Instrument verification'] button").ClickAsync(new());
        Assert.That(cut.Markup, Does.Contain("Recognised"));
        Assert.That(_http.RequestCount, Is.EqualTo(2));
    }

    [Test]
    public async Task VerifyAsync_SessionExpired_OffersSignInWithoutClearingInputs()
    {
        _http.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"));
        IRenderedComponent<InstrumentVerification> cut = Render();

        await cut.Find("button").ClickAsync(new());

        Assert.That(cut.Find("a").TextContent, Is.EqualTo("Sign in again"));
        Assert.That(cut.Markup, Does.Contain("session has expired"));
    }

    private IRenderedComponent<InstrumentVerification> Render() => _context.Render<InstrumentVerification>(parameters => parameters
        .Add(component => component.Symbol, "DEMO-2")
        .Add(component => component.Exchange, "XTEST")
        .Add(component => component.QuoteCurrency, "USD"));

    private static HttpResponseMessage Json(InstrumentVerificationResponse response) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(response)
    };

    private static InstrumentVerificationResponse Recognised() => new("DEMO-2", "XTEST", "USD", "resolved", null,
        "eToro", "DEMO-2", "Test Exchange", 5, "Synthetic Demo", "USD", true, null, "open", null,
        new VerificationQuoteDto(12.345m, null, "USD", DateTimeOffset.Parse("2026-01-02T12:00:00Z"),
            DateTimeOffset.Parse("2026-01-02T11:45:00Z"), "delayed"), null);
}
