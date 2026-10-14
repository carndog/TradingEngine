using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Api.IntegrationTests.RateLimiting;
using TradingEngine.Api.RateLimiting;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.IntegrationTests.WatchedInstruments;

[TestFixture]
public sealed class WatchedInstrumentBulkDeleteEndpointTests
{
    private const string Route = "/api/watched-instruments/bulk-delete";

    private static readonly Guid FirstId = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondId = Guid.Parse("c1000000-0000-0000-0000-000000000002");

    private WebApplicationFactory<Program> _factory = null!;
    private StubWatchedInstrumentStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new StubWatchedInstrumentStore();
        _factory = CreateFactory(_store);
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        StubWatchedInstrumentStore store,
        IDictionary<string, string?>? settings = null,
        IRequestBudgetLimiter? budgetLimiter = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSyntheticConnectionString();
                if (settings is not null)
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                        config.AddInMemoryCollection(settings));
                }

                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IWatchedInstrumentStore>(store);
                    if (budgetLimiter is not null)
                    {
                        services.AddSingleton<IRequestBudgetLimiter>(budgetLimiter);
                    }
                });
            });
    }

    [Test]
    public async Task BulkDelete_WithValidItems_ReturnsOutcomeAndPassesDecodedTokens()
    {
        _store.DeleteResult = new WatchedInstrumentBulkDeleteOutcome([FirstId], [SecondId]);
        HttpClient client = _factory.CreateClient();
        BulkDeleteWatchedInstrumentsRequest request = new(
        [
            new BulkDeleteWatchedInstrumentItemDto(FirstId, Convert.ToBase64String([1, 2, 3])),
            new BulkDeleteWatchedInstrumentItemDto(SecondId, Convert.ToBase64String([4, 5, 6]))
        ]);

        HttpResponseMessage response = await client.PostAsJsonAsync(Route, request);
        BulkDeleteWatchedInstrumentsResponse? body = await response.Content
            .ReadFromJsonAsync<BulkDeleteWatchedInstrumentsResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.DeletedIds, Is.EqualTo(new[] { FirstId }));
            Assert.That(body.AlreadyDeletedIds, Is.EqualTo(new[] { SecondId }));
            Assert.That(_store.RequestedDelete, Is.Not.Null);
            Assert.That(
                _store.RequestedDelete!.Value.Items.Select(item => item.InstrumentId),
                Is.EqualTo(new[] { FirstId, SecondId }));
            Assert.That(
                _store.RequestedDelete.Value.Items[0].ExpectedConcurrencyToken,
                Is.EqualTo(new byte[] { 1, 2, 3 }));
        });
    }

    [Test]
    public async Task BulkDelete_WithNullBody_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, Route);
        request.Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.SendAsync(request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(ProblemCode(problem), Is.EqualTo("watched_instrument.request_required"));
            Assert.That(_store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WithEmptyItems_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new BulkDeleteWatchedInstrumentsRequest([]));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.delete_items_required"));
            Assert.That(_store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WithMoreThan100Items_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        BulkDeleteWatchedInstrumentItemDto[] items = Enumerable.Range(0, 101)
            .Select(index => new BulkDeleteWatchedInstrumentItemDto(
                Guid.Parse($"c2000000-0000-0000-0000-{index:D12}"),
                Convert.ToBase64String([1])))
            .ToArray();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new BulkDeleteWatchedInstrumentsRequest(items));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.delete_items_too_large"));
            Assert.That(_store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WithDuplicateIds_ReturnsBadRequestIdentifyingIds()
    {
        HttpClient client = _factory.CreateClient();
        string token = Convert.ToBase64String([1]);
        BulkDeleteWatchedInstrumentsRequest request = new(
        [
            new BulkDeleteWatchedInstrumentItemDto(FirstId, token),
            new BulkDeleteWatchedInstrumentItemDto(SecondId, token),
            new BulkDeleteWatchedInstrumentItemDto(FirstId, token)
        ]);

        HttpResponseMessage response = await client.PostAsJsonAsync(Route, request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.delete_items_duplicate"));
            Assert.That(ProblemIds(problem), Is.EqualTo(new[] { FirstId }));
            Assert.That(_store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WithEmptyGuid_ReturnsBadRequest()
    {
        HttpClient client = _factory.CreateClient();
        BulkDeleteWatchedInstrumentsRequest request = new(
        [
            new BulkDeleteWatchedInstrumentItemDto(Guid.Empty, Convert.ToBase64String([1]))
        ]);

        HttpResponseMessage response = await client.PostAsJsonAsync(Route, request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.delete_items_invalid"));
            Assert.That(_store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WithMissingToken_ReturnsPreconditionRequiredIdentifyingIds()
    {
        HttpClient client = _factory.CreateClient();
        BulkDeleteWatchedInstrumentsRequest request = new(
        [
            new BulkDeleteWatchedInstrumentItemDto(FirstId, Convert.ToBase64String([1])),
            new BulkDeleteWatchedInstrumentItemDto(SecondId, null)
        ]);

        HttpResponseMessage response = await client.PostAsJsonAsync(Route, request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(
                response.StatusCode,
                Is.EqualTo((HttpStatusCode)StatusCodes.Status428PreconditionRequired));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.delete_token_required"));
            Assert.That(ProblemIds(problem), Is.EqualTo(new[] { SecondId }));
            Assert.That(_store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WithMalformedToken_ReturnsBadRequestIdentifyingIds()
    {
        HttpClient client = _factory.CreateClient();
        BulkDeleteWatchedInstrumentsRequest request = new(
        [
            new BulkDeleteWatchedInstrumentItemDto(FirstId, "!!!not-base64!!!")
        ]);

        HttpResponseMessage response = await client.PostAsJsonAsync(Route, request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.delete_token_invalid"));
            Assert.That(ProblemIds(problem), Is.EqualTo(new[] { FirstId }));
            Assert.That(_store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WhenStoreReportsUnknownIds_ReturnsNotFoundIdentifyingIds()
    {
        _store.DeleteResult = WatchedInstrumentErrors.DeleteItemsNotFound
            .WithExtension("instrumentIds", new[] { SecondId });
        HttpClient client = _factory.CreateClient();
        string token = Convert.ToBase64String([1]);
        BulkDeleteWatchedInstrumentsRequest request = new(
        [
            new BulkDeleteWatchedInstrumentItemDto(FirstId, token),
            new BulkDeleteWatchedInstrumentItemDto(SecondId, token)
        ]);

        HttpResponseMessage response = await client.PostAsJsonAsync(Route, request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.delete_items_not_found"));
            Assert.That(ProblemIds(problem), Is.EqualTo(new[] { SecondId }));
        });
    }

    [Test]
    public async Task BulkDelete_WhenStoreReportsStaleItems_ReturnsPreconditionFailedIdentifyingIds()
    {
        _store.DeleteResult = WatchedInstrumentErrors.DeleteItemsStale
            .WithExtension("instrumentIds", new[] { FirstId });
        HttpClient client = _factory.CreateClient();
        string token = Convert.ToBase64String([1]);
        BulkDeleteWatchedInstrumentsRequest request = new(
        [
            new BulkDeleteWatchedInstrumentItemDto(FirstId, token),
            new BulkDeleteWatchedInstrumentItemDto(SecondId, token)
        ]);

        HttpResponseMessage response = await client.PostAsJsonAsync(Route, request);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(
                response.StatusCode,
                Is.EqualTo((HttpStatusCode)StatusCodes.Status412PreconditionFailed));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("watched_instrument.delete_items_stale"));
            Assert.That(ProblemIds(problem), Is.EqualTo(new[] { FirstId }));
        });
    }

    [Test]
    public async Task BulkDelete_FromCrossSite_ReturnsForbiddenWithoutTouchingStore()
    {
        HttpClient client = _factory.CreateClient();
        HttpRequestMessage request = new(HttpMethod.Post, Route)
        {
            Content = JsonContent.Create(new BulkDeleteWatchedInstrumentsRequest(
            [
                new BulkDeleteWatchedInstrumentItemDto(FirstId, Convert.ToBase64String([1]))
            ]))
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
            Assert.That(_store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WithoutPrincipalWhenTrustEnabled_ReturnsUnauthorized()
    {
        StubWatchedInstrumentStore store = new();
        using WebApplicationFactory<Program> factory = CreateFactory(
            store,
            new Dictionary<string, string?>
            {
                ["Authentication:EasyAuth:TrustPlatformHeaders"] = "true"
            });
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new BulkDeleteWatchedInstrumentsRequest(
            [
                new BulkDeleteWatchedInstrumentItemDto(FirstId, Convert.ToBase64String([1]))
            ]));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(
                ProblemCode(problem),
                Is.EqualTo("authentication.principal_required"));
            Assert.That(store.RequestedDelete, Is.Null);
        });
    }

    [Test]
    public async Task BulkDelete_WhenRateLimited_ReturnsTooManyRequestsWithoutTouchingStore()
    {
        StubWatchedInstrumentStore store = new();
        ScriptedRequestBudgetLimiter limiter = new(
            ScriptedRateLimitLease.Rejected(TimeSpan.FromSeconds(30)));
        using WebApplicationFactory<Program> factory = CreateFactory(
            store,
            budgetLimiter: limiter);
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new BulkDeleteWatchedInstrumentsRequest(
            [
                new BulkDeleteWatchedInstrumentItemDto(FirstId, Convert.ToBase64String([1]))
            ]));
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)429));
            Assert.That(ProblemCode(problem), Is.EqualTo("rate_limit.exceeded"));
            Assert.That(store.RequestedDelete, Is.Null);
        });
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

    private static Guid[]? ProblemIds(ProblemDetails? problem)
    {
        if (problem?.Extensions.TryGetValue("instrumentIds", out object? ids) is true
            && ids is JsonElement element
            && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(item => item.GetGuid())
                .ToArray();
        }

        return null;
    }
}
