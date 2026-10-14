using System.Net.Http.Json;
using System.Text.Json;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Services;

public sealed class TradingEngineApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public TradingEngineApiClient(HttpClient http)
    {
        _http = http;
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

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                T? value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return ApiResult<T>.Ok(value!);
            }

            ApiError error = await ReadErrorAsync(response, cancellationToken);
            return ApiResult<T>.Fail(error);
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Fail(ApiError.Network("The API could not be reached. Check your connection and try again."));
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested is false)
        {
            return ApiResult<T>.Fail(ApiError.Network("The request timed out. Try again."));
        }
        catch (TaskCanceledException)
        {
            throw;
        }
    }

    private static async Task<ApiError> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        int status = (int)response.StatusCode;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;

        try
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text) is false)
            {
                using JsonDocument document = JsonDocument.Parse(text);
                JsonElement root = document.RootElement;
                string? detail = TryGetString(root, "detail") ?? TryGetString(root, "title");
                string? code = TryGetString(root, "code");
                return ApiError.Create(status, code, detail, retryAfter);
            }
        }
        catch (JsonException)
        {
        }

        return ApiError.Create(status, null, null, retryAfter);
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
