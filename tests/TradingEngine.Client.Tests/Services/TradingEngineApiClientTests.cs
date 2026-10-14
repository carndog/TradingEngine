using System.Net;
using TradingEngine.Client.Services;
using TradingEngine.Contracts.Session;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Tests.Services;

[TestFixture]
public sealed class TradingEngineApiClientTests
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan[] NoRetries = [];

    private StubHttpMessageHandler _handler = null!;
    private TradingEngineApiClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHttpMessageHandler();
        _client = CreateClient(_handler);
    }

    [Test]
    public async Task GetSessionAsync_WhenSuccess_ReturnsSession()
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"mode":"entra","displayName":"owner@example.test"}"""));

        ApiResult<SessionResponse> result = await _client.GetSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.Mode, Is.EqualTo("entra"));
            Assert.That(result.Value.DisplayName, Is.EqualTo("owner@example.test"));
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenSuccessIsHtml_ReturnsUnexpectedError()
    {
        _handler.Enqueue(StubHttpMessageHandler.Html(
            HttpStatusCode.OK,
            "<html><body>easy auth</body></html>"));

        ApiResult<WatchedInstrumentPageResponse> result =
            await _client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.Code, Is.EqualTo("response.unexpected"));
            Assert.That(result.Error.Reference, Is.Not.Null);
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenSuccessBodyMalformed_ReturnsUnexpectedError()
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"items": ["""));

        ApiResult<WatchedInstrumentPageResponse> result =
            await _client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.Code, Is.EqualTo("response.unexpected"));
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenSuccessBodyEmpty_ReturnsUnexpectedError()
    {
        _handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("", System.Text.Encoding.UTF8, "application/json")
        });

        ApiResult<WatchedInstrumentPageResponse> result =
            await _client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.Code, Is.EqualTo("response.unexpected"));
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenSuccessBodyIsNullLiteral_ReturnsUnexpectedError()
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.OK, "null"));

        ApiResult<WatchedInstrumentPageResponse> result =
            await _client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.Code, Is.EqualTo("response.unexpected"));
        });
    }

    [TestCase("\"denied\"")]
    [TestCase("[1,2,3]")]
    [TestCase("42")]
    public async Task SearchInstrumentsAsync_WhenErrorBodyIsNotAnObject_ReturnsStatusError(
        string body)
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.BadRequest, body));

        ApiResult<WatchedInstrumentPageResponse> result =
            await _client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.Status, Is.EqualTo(400));
            Assert.That(result.Error.Code, Is.Null);
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenProblemHasTraceId_SetsReference()
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.InternalServerError,
            """{"detail":"An unexpected error occurred.","code":"server.internal_error","traceId":"trace-abc"}"""));
        _handler.Enqueue(StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));

        ApiResult<WatchedInstrumentPageResponse> result =
            await _client.SearchInstrumentsAsync(null, 1, 25);

        Assert.That(result.Error!.Reference, Is.EqualTo("trace-abc"));
    }

    [Test]
    public async Task GetInstrumentAsync_WhenUnauthorized_ReturnsUnauthorizedError()
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.Unauthorized,
            """{"detail":"The platform authentication principal header is missing.","code":"authentication.principal_required"}"""));

        ApiResult<WatchedInstrumentResponse> result =
            await _client.GetInstrumentAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.IsUnauthorized, Is.True);
            Assert.That(result.Error.IsForbidden, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo("authentication.principal_required"));
            Assert.That(_handler.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetInstrumentAsync_WhenForbidden_ReturnsForbiddenErrorWithoutRetry()
    {
        _handler.Enqueue(StubHttpMessageHandler.Html(
            HttpStatusCode.Forbidden,
            "<html><body>allowlist rejected</body></html>"));

        ApiResult<WatchedInstrumentResponse> result =
            await _client.GetInstrumentAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.IsForbidden, Is.True);
            Assert.That(result.Error.IsUnauthorized, Is.False);
            Assert.That(_handler.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenTransientThenSuccess_RetriesGet()
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.ServiceUnavailable, """{"detail":"down"}"""));
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"items":[],"page":1,"pageSize":25,"totalCount":0}"""));

        TradingEngineApiClient client = CreateClient(
            _handler,
            retryDelays: [TimeSpan.FromMilliseconds(1)]);
        ApiResult<WatchedInstrumentPageResponse> result =
            await client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_handler.RequestCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenBadRequest_DoesNotRetry()
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.BadRequest,
            """{"detail":"bad","code":"watched_instrument.page_invalid"}"""));

        TradingEngineApiClient client = CreateClient(
            _handler,
            retryDelays: [TimeSpan.FromMilliseconds(1)]);
        ApiResult<WatchedInstrumentPageResponse> result =
            await client.SearchInstrumentsAsync(null, 0, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Status, Is.EqualTo(400));
            Assert.That(_handler.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RegisterInstrumentAsync_WhenServiceUnavailable_DoesNotRetryMutation()
    {
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.ServiceUnavailable, """{"detail":"down"}"""));

        TradingEngineApiClient client = CreateClient(
            _handler,
            retryDelays: [TimeSpan.FromMilliseconds(1)]);
        ApiResult<WatchedInstrumentResponse> result = await client.RegisterInstrumentAsync(
            new RegisterWatchedInstrumentRequest("SYN", "XTEST", "USD", 60, "configured", 4, [], []));

        Assert.Multiple(() =>
        {
            Assert.That(result.Error!.Status, Is.EqualTo(503));
            Assert.That(_handler.RequestCount, Is.EqualTo(1));
            Assert.That(_handler.Methods[0], Is.EqualTo(HttpMethod.Post));
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenCancelled_PropagatesCancellation()
    {
        _handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        });

        using CancellationTokenSource source = new(TimeSpan.FromMilliseconds(50));

        OperationCanceledException? exception = await Assert.CatchAsync<OperationCanceledException>(
            () => _client.SearchInstrumentsAsync(null, 1, 25, source.Token));

        Assert.That(exception, Is.Not.Null);
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenRequestTimesOut_ReturnsTimeoutError()
    {
        _handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        });

        TradingEngineApiClient client = CreateClient(
            _handler,
            requestTimeout: TimeSpan.FromMilliseconds(50));
        ApiResult<WatchedInstrumentPageResponse> result =
            await client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.IsNetworkError, Is.True);
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenHandlerThrowsUnexpectedly_ReturnsUnexpectedError()
    {
        _handler.Enqueue((_, _) => throw new InvalidOperationException("synthetic failure"));

        TradingEngineApiClient client = CreateClient(
            _handler,
            retryDelays: [TimeSpan.FromMilliseconds(1)]);
        ApiResult<WatchedInstrumentPageResponse> result =
            await client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error!.Code, Is.EqualTo("response.unexpected"));
            Assert.That(result.Error.Reference, Is.Not.Null.And.StartsWith("ref-"));
        });
    }

    [Test]
    public async Task SearchInstrumentsAsync_WhenRateLimited_HonoursBoundedRetryAfter()
    {
        HttpResponseMessage limited = StubHttpMessageHandler.Json(
            (HttpStatusCode)429, """{"detail":"slow down"}""");
        limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromMilliseconds(1));
        _handler.Enqueue(limited);
        _handler.Enqueue(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"items":[],"page":1,"pageSize":25,"totalCount":0}"""));

        TradingEngineApiClient client = CreateClient(
            _handler,
            retryDelays: [TimeSpan.FromMilliseconds(1)]);
        ApiResult<WatchedInstrumentPageResponse> result =
            await client.SearchInstrumentsAsync(null, 1, 25);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_handler.RequestCount, Is.EqualTo(2));
        });
    }

    private static TradingEngineApiClient CreateClient(
        StubHttpMessageHandler handler,
        TimeSpan? requestTimeout = null,
        TimeSpan[]? retryDelays = null)
    {
        HttpClient http = new(handler)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        return new TradingEngineApiClient(
            http,
            requestTimeout ?? LongTimeout,
            retryDelays ?? NoRetries);
    }
}
