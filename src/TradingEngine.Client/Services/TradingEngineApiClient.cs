using System.Net.Http.Json;
using System.Text.Json;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.Session;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Services;

public sealed class TradingEngineApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan[] DefaultReadRetryDelays =
        [TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1200)];
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan[] _readRetryDelays;
    private readonly IUiClock _clock;
    private readonly SessionState? _sessionState;

    public TradingEngineApiClient(
        HttpClient http,
        SessionState sessionState,
        IUiClock clock)
        : this(http, DefaultRequestTimeout, DefaultReadRetryDelays, clock, sessionState)
    {
    }

    internal TradingEngineApiClient(
        HttpClient http,
        TimeSpan requestTimeout,
        TimeSpan[] readRetryDelays,
        IUiClock? clock = null,
        SessionState? sessionState = null)
    {
        _http = http;
        _requestTimeout = requestTimeout;
        _readRetryDelays = readRetryDelays;
        _clock = clock ?? SystemUiClock.Instance;
        _sessionState = sessionState;
    }

    public Task<ApiResult<WatchedInstrumentPageResponse>> SearchInstrumentsAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        string path = $"api/watched-instruments?page={page}&pageSize={pageSize}";
        if (string.IsNullOrWhiteSpace(query) is false)
        {
            path += $"&q={Uri.EscapeDataString(query.Trim())}";
        }

        return SendAsync<WatchedInstrumentPageResponse>(
            HttpMethod.Get,
            path,
            body: null,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<WatchedInstrumentResponse>> GetInstrumentAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<WatchedInstrumentResponse>(
            HttpMethod.Get,
            $"api/watched-instruments/{id}",
            body: null,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<WatchedInstrumentSettingsResponse>> GetSettingsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<WatchedInstrumentSettingsResponse>(
            HttpMethod.Get,
            $"api/watched-instruments/{id}/settings",
            body: null,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<WatchedInstrumentSettingsResponse>> UpdateSettingsAsync(
        Guid id,
        UpdateWatchedInstrumentSettingsRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<WatchedInstrumentSettingsResponse>(
            HttpMethod.Put,
            $"api/watched-instruments/{id}/settings",
            request,
            concurrencyToken,
            cancellationToken);
    }

    public Task<ApiResult<BulkDeleteWatchedInstrumentsResponse>> BulkDeleteInstrumentsAsync(
        BulkDeleteWatchedInstrumentsRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<BulkDeleteWatchedInstrumentsResponse>(
            HttpMethod.Post,
            "api/watched-instruments/bulk-delete",
            request,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<WatchedInstrumentResponse>> RegisterInstrumentAsync(
        RegisterWatchedInstrumentRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<WatchedInstrumentResponse>(
            HttpMethod.Post,
            "api/watched-instruments",
            request,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleTimelineResponse>> GetTimelineAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleTimelineResponse>(
            HttpMethod.Get,
            $"api/watched-instruments/{instrumentId}/monitoring-rule",
            body: null,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleDraftsResponse>> GetDraftsAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleDraftsResponse>(
            HttpMethod.Get,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/drafts",
            body: null,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleRevisionResponse>> GetApplicableRevisionAsync(
        Guid instrumentId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleRevisionResponse>(
            HttpMethod.Get,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/revisions/applicable",
            body: null,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleRevisionResponse>> GetRevisionAsync(
        Guid instrumentId,
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleRevisionResponse>(
            HttpMethod.Get,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/revisions/{revisionId}",
            body: null,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleTimelineResponse>> CreateDraftAsync(
        Guid instrumentId,
        MonitoringRuleDraftRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleTimelineResponse>(
            HttpMethod.Post,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/drafts",
            request,
            concurrencyToken,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleTimelineResponse>> UpdateDraftAsync(
        Guid instrumentId,
        Guid draftId,
        MonitoringRuleDraftRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleTimelineResponse>(
            HttpMethod.Put,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/drafts/{draftId}",
            request,
            concurrencyToken,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleTimelineResponse>> DeleteDraftAsync(
        Guid instrumentId,
        Guid draftId,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleTimelineResponse>(
            HttpMethod.Delete,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/drafts/{draftId}",
            body: null,
            ifMatch: concurrencyToken,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleTimelineResponse>> ApplyDraftAsync(
        Guid instrumentId,
        Guid draftId,
        ApplyMonitoringRuleRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleTimelineResponse>(
            HttpMethod.Post,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/drafts/{draftId}/apply",
            request,
            concurrencyToken,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleTimelineResponse>> ScheduleDraftAsync(
        Guid instrumentId,
        Guid draftId,
        ScheduleMonitoringRuleRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleTimelineResponse>(
            HttpMethod.Post,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/drafts/{draftId}/schedule",
            request,
            concurrencyToken,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleTimelineResponse>> UpdateRevisionAsync(
        Guid instrumentId,
        Guid revisionId,
        EditMonitoringRuleRevisionRequest request,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleTimelineResponse>(
            HttpMethod.Put,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/revisions/{revisionId}",
            request,
            concurrencyToken,
            cancellationToken);
    }

    public Task<ApiResult<SessionResponse>> GetSessionAsync(
        CancellationToken cancellationToken = default)
    {
        return SendAsync<SessionResponse>(
            HttpMethod.Get,
            "api/session",
            body: null,
            ifMatch: null,
            cancellationToken);
    }

    public Task<ApiResult<MonitoringRuleTimelineResponse>> RemoveRevisionAsync(
        Guid instrumentId,
        Guid revisionId,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<MonitoringRuleTimelineResponse>(
            HttpMethod.Delete,
            $"api/watched-instruments/{instrumentId}/monitoring-rule/revisions/{revisionId}",
            body: null,
            ifMatch: concurrencyToken,
            cancellationToken);
    }

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource bounded =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_requestTimeout);

        int retries = method == HttpMethod.Get ? _readRetryDelays.Length : 0;
        DateTimeOffset started = _clock.UtcNow;

        for (int attempt = 0; ; attempt++)
        {
            ApiResult<T> outcome = await SendOnceAsync<T>(
                method,
                path,
                body,
                ifMatch,
                cancellationToken,
                bounded.Token);

            if (attempt >= retries || outcome.IsSuccess || IsTransient(outcome.Error!) is false)
            {
                return outcome;
            }

            TimeSpan? serverDelay = ResolveServerDelay(outcome.Error!);
            if (serverDelay is not null)
            {
                TimeSpan remaining = _requestTimeout - (_clock.UtcNow - started);
                if (serverDelay.Value > remaining || serverDelay.Value > MaximumRetryDelay)
                {
                    return outcome;
                }
            }

            TimeSpan delay = serverDelay ?? _readRetryDelays[attempt];
            try
            {
                await Task.Delay(delay, bounded.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ApiResult<T>.Fail(ApiError.Network("The request timed out. Try again."));
            }
        }
    }

    private async Task<ApiResult<T>> SendOnceAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? ifMatch,
        CancellationToken cancellationToken,
        CancellationToken boundedToken)
    {
        try
        {
            using HttpRequestMessage request = new(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            if (string.IsNullOrWhiteSpace(ifMatch) is false)
            {
                request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
            }

            using HttpResponseMessage response = await _http.SendAsync(request, boundedToken);
            if (response.IsSuccessStatusCode)
            {
                return await ReadSuccessAsync<T>(response, boundedToken);
            }

            ApiError error = await ReadErrorAsync(response, boundedToken, _clock.UtcNow);
            if (error.IsUnauthorized || error.IsForbidden)
            {
                _sessionState?.Invalidate();
            }

            return ApiResult<T>.Fail(error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ApiResult<T>.Fail(ApiError.Network("The request timed out. Try again."));
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Fail(ApiError.Network("The API could not be reached. Check your connection and try again."));
        }
        catch (Exception)
        {
            return ApiResult<T>.Fail(ApiError.Unexpected(
                "The request failed unexpectedly. Try again.",
                NewReference()));
        }
    }

    private static async Task<ApiResult<T>> ReadSuccessAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null
            || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) is false)
        {
            return ApiResult<T>.Fail(ApiError.Unexpected(
                "The server returned a response that is not JSON.",
                NewReference()));
        }

        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ApiResult<T>.Fail(ApiError.Unexpected(
                "The server returned an empty response.",
                NewReference()));
        }

        try
        {
            T? value = JsonSerializer.Deserialize<T>(text, JsonOptions);
            return value is null
                ? ApiResult<T>.Fail(ApiError.Unexpected(
                    "The server returned an empty result.",
                    NewReference()))
                : ApiResult<T>.Ok(value);
        }
        catch (JsonException)
        {
            return ApiResult<T>.Fail(ApiError.Unexpected(
                "The server returned a malformed response.",
                NewReference()));
        }
    }

    private static async Task<ApiError> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        DateTimeOffset receivedAt)
    {
        int status = (int)response.StatusCode;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        DateTimeOffset? retryAfterUtc =
            response.Headers.RetryAfter?.Date ??
            (retryAfter is { } delta ? receivedAt + delta : null);

        try
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text) is false)
            {
                using JsonDocument document = JsonDocument.Parse(text);
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    string? detail = TryGetString(root, "detail") ?? TryGetString(root, "title");
                    string? code = TryGetString(root, "code");
                    string? traceId = TryGetString(root, "traceId");
                    return ApiError.Create(status, code, detail, retryAfter, traceId, retryAfterUtc);
                }
            }
        }
        catch (JsonException)
        {
        }

        return ApiError.Create(status, null, null, retryAfter, retryAfterUtc: retryAfterUtc);
    }

    private static bool IsTransient(ApiError error)
    {
        return error.IsNetworkError || error.Status is 408 or 429 or 500 or 502 or 503 or 504;
    }

    private TimeSpan? ResolveServerDelay(ApiError error)
    {
        if (error.RetryAfterUtc is DateTimeOffset retryAt)
        {
            TimeSpan delta = retryAt - _clock.UtcNow;
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        return error.RetryAfter;
    }

    private static string NewReference()
    {
        return $"ref-{Guid.NewGuid():N}"[..13];
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
