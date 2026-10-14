using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Application.Ports;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class FetchSiteGuardEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private StubWatchedInstrumentStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new StubWatchedInstrumentStore();
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSyntheticConnectionString();
                builder.ConfigureServices(services =>
                    services.AddSingleton<IWatchedInstrumentStore>(_store));
            });
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task PostWatchedInstrument_FromCrossSite_ReturnsForbidden()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, "/api/watched-instruments")
        {
            Content = JsonContent.Create(CreateRequest())
        };
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("request.cross_site_write_rejected"));
            Assert.That(_store.AddedRegistration, Is.Null);
        });
    }

    [Test]
    public async Task PostWatchedInstrument_FromSameOrigin_Proceeds()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, "/api/watched-instruments")
        {
            Content = JsonContent.Create(CreateRequest())
        };
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(_store.AddedRegistration, Is.Not.Null);
    }

    [Test]
    public async Task PostWatchedInstrument_WithoutFetchSiteHeader_Proceeds()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/watched-instruments",
            CreateRequest());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(_store.AddedRegistration, Is.Not.Null);
    }

    [Test]
    public async Task GetWatchedInstruments_FromCrossSite_Proceeds()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Get, "/api/watched-instruments");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static RegisterWatchedInstrumentRequest CreateRequest()
    {
        return new RegisterWatchedInstrumentRequest(
            "demo-2",
            "xtest",
            "gbp",
            60,
            "configured",
            4,
            [
                new ChartZoneDto(
                    "support-a",
                    95m,
                    100m,
                    105m,
                    [
                        new ChartConditionDto("buy-zone", "publish-signal"),
                        new ChartConditionDto("support-loss", "publish-signal")
                    ])
            ],
            []);
    }

    private static string? ProblemCode(ProblemDetails? problem)
    {
        if (problem?.Extensions.TryGetValue("code", out object? code) is true
            && code is JsonElement element)
        {
            return element.GetString();
        }

        return null;
    }
}
