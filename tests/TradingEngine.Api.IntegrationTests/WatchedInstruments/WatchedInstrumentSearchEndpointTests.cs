using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class WatchedInstrumentSearchEndpointTests
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
    public async Task GetWatchedInstruments_WithoutParameters_UsesDefaultPaging()
    {
        _store.SearchResult = new WatchedInstrumentPage([], 1, 25, 0);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watched-instruments");
        WatchedInstrumentPageResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentPageResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Page, Is.EqualTo(1));
            Assert.That(body.PageSize, Is.EqualTo(25));
            Assert.That(body.TotalCount, Is.EqualTo(0));
            Assert.That(body.Items, Is.Empty);
            Assert.That(
                _store.RequestedSearch,
                Is.EqualTo(((string?)null, 1, 25)));
        });
    }

    [Test]
    public async Task GetWatchedInstruments_WithQuery_PassesTrimmedQueryToStore()
    {
        _store.SearchResult = new WatchedInstrumentPage([], 1, 25, 0);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/watched-instruments?q=%20%20btc%20&page=3&pageSize=10");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                _store.RequestedSearch,
                Is.EqualTo(("btc", 3, 10)));
        });
    }

    [Test]
    public async Task GetWatchedInstruments_ReturnsSummaryRows()
    {
        WatchedInstrumentSummary first = CreateSummary("DEMO-1", "XTEST", MonitoringState.Configured);
        WatchedInstrumentSummary second = CreateSummary("DEMO-2", "XTEST", MonitoringState.Monitored);
        _store.SearchResult = new WatchedInstrumentPage(
            [first, second],
            2,
            10,
            21);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/watched-instruments?page=2&pageSize=10");
        WatchedInstrumentPageResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentPageResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Items, Has.Count.EqualTo(2));
            Assert.That(body.Page, Is.EqualTo(2));
            Assert.That(body.PageSize, Is.EqualTo(10));
            Assert.That(body.TotalCount, Is.EqualTo(21));
            Assert.That(body.Items[0].Id, Is.EqualTo(first.Id));
            Assert.That(body.Items[0].Symbol, Is.EqualTo("DEMO-1"));
            Assert.That(body.Items[0].Exchange, Is.EqualTo("XTEST"));
            Assert.That(body.Items[0].QuoteCurrency, Is.EqualTo("GBP"));
            Assert.That(body.Items[0].MonitoringState, Is.EqualTo("configured"));
            Assert.That(body.Items[0].SamplingIntervalSeconds, Is.EqualTo(60));
            Assert.That(body.Items[1].MonitoringState, Is.EqualTo("monitored"));
        });
    }

    [Test]
    public async Task GetWatchedInstruments_BeyondLastPage_ReturnsEmptyItemsWithTotals()
    {
        _store.SearchResult = new WatchedInstrumentPage([], 9, 25, 41);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/watched-instruments?page=9");
        WatchedInstrumentPageResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentPageResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Items, Is.Empty);
            Assert.That(body.TotalCount, Is.EqualTo(41));
        });
    }

    [Test]
    public async Task GetWatchedInstruments_WithZeroPage_ReturnsValidationProblem()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/watched-instruments?page=0");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.page_invalid"));
            Assert.That(_store.RequestedSearch, Is.Null);
        });
    }

    [TestCase(0)]
    [TestCase(101)]
    public async Task GetWatchedInstruments_WithOutOfRangePageSize_ReturnsValidationProblem(
        int pageSize)
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments?pageSize={pageSize}");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.page_size_out_of_range"));
            Assert.That(_store.RequestedSearch, Is.Null);
        });
    }

    [TestCase("page=2147483647")]
    [TestCase("page=21474838&pageSize=100")]
    public async Task GetWatchedInstruments_WithUnaddressablePage_ReturnsValidationProblem(
        string parameters)
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments?{parameters}");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.page_out_of_range"));
            Assert.That(_store.RequestedSearch, Is.Null);
        });
    }

    [Test]
    public async Task GetWatchedInstruments_WithAddressableLargePage_QueriesStore()
    {
        _store.SearchResult = new WatchedInstrumentPage([], 21474837, 100, 41);
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/watched-instruments?page=21474837&pageSize=100");
        WatchedInstrumentPageResponse? body = await response.Content
            .ReadFromJsonAsync<WatchedInstrumentPageResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body!.Items, Is.Empty);
            Assert.That(_store.RequestedSearch, Is.EqualTo(((string?)null, 21474837, 100)));
        });
    }

    [Test]
    public async Task GetWatchedInstruments_WhenStoreThrows_ReturnsInternalErrorProblem()
    {
        _store.SearchException = new InvalidOperationException("synthetic store failure");
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/watched-instruments");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(body, Does.Contain("server.internal_error"));
            Assert.That(body, Does.Contain("traceId"));
            Assert.That(body, Does.Not.Contain("synthetic store failure"));
        });
    }

    [Test]
    public async Task GetWatchedInstruments_WithOversizedQuery_ReturnsValidationProblem()
    {
        HttpClient client = _factory.CreateClient();
        string query = new('x', 129);

        HttpResponseMessage response = await client.GetAsync(
            $"/api/watched-instruments?q={query}");
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.search_query_too_long"));
            Assert.That(_store.RequestedSearch, Is.Null);
        });
    }

    private static WatchedInstrumentSummary CreateSummary(
        string symbol,
        string exchange,
        MonitoringState state)
    {
        return new WatchedInstrumentSummary(
            Guid.Parse("3a5d8e2f-9c41-4b6e-8f7a-2b3c4d5e6f01"),
            symbol,
            exchange,
            "GBP",
            state,
            60);
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
