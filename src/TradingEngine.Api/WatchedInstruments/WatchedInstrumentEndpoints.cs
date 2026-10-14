using Microsoft.AspNetCore.Http.HttpResults;
using NodaTime;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Application.WatchedInstruments.Read;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Application.WatchedInstruments.Search;
using TradingEngine.Application.WatchedInstruments.Settings;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.WatchedInstruments;

internal static class WatchedInstrumentEndpoints
{
    private const string GroupPath = "/api/watched-instruments";
    private const int SearchQueryMaximumLength = 128;
    private const int MaximumPageSize = 100;

    public static RouteGroupBuilder MapWatchedInstrumentEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup(GroupPath);
        group.MapPost("", RegisterAsync);
        group.MapGet("", SearchAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/settings", GetSettingsAsync);
        group.MapPut("/{id:guid}/settings", PutSettingsAsync);

        return group;
    }

    private static async Task<Results<Created<WatchedInstrumentResponse>, ProblemHttpResult>> RegisterAsync(
        RegisterWatchedInstrumentRequest? request,
        RegisterWatchedInstrumentHandler handler,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.RequestRequired);
        }

        Result<ChartAnalysisDefinition> definition = ChartAnalysisMapping.ToDefinition(
            request.PriceScale,
            request.SupportZones,
            request.ResistanceZones);
        if (definition.IsFailure)
        {
            return ApiProblemDetails.Problem(definition.Error);
        }

        Result<MonitoringState> monitoringState = MapMonitoringState(request.MonitoringState);
        if (monitoringState.IsFailure)
        {
            return ApiProblemDetails.Problem(monitoringState.Error);
        }

        RegisterWatchedInstrument command = new(
            request.Symbol ?? string.Empty,
            request.Exchange ?? string.Empty,
            request.QuoteCurrency ?? string.Empty,
            request.SamplingIntervalSeconds,
            monitoringState.Value,
            definition.Value);

        Result<WatchedInstrument> registered = await handler.HandleAsync(command, cancellationToken);
        if (registered.IsFailure)
        {
            return ApiProblemDetails.Problem(registered.Error);
        }

        WatchedInstrumentResponse response = ToResponse(registered.Value, definition.Value);

        return TypedResults.Created($"{GroupPath}/{registered.Value.Id}", response);
    }

    private static async Task<Results<Ok<WatchedInstrumentResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        GetWatchedInstrumentHandler handler,
        CancellationToken cancellationToken)
    {
        Result<WatchedInstrumentConfiguration> configuration = await handler.HandleAsync(
            new GetWatchedInstrument(id),
            cancellationToken);

        if (configuration.IsFailure)
        {
            return ApiProblemDetails.Problem(configuration.Error);
        }

        return TypedResults.Ok(ToResponse(
            configuration.Value.Instrument,
            configuration.Value.Definition));
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        int? page,
        int? pageSize,
        SearchWatchedInstrumentsHandler handler,
        CancellationToken cancellationToken)
    {
        int requestedPage = page ?? 1;
        int requestedPageSize = pageSize ?? 25;
        string? query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        if (requestedPage < 1)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.PageInvalid);
        }

        if (requestedPageSize is < 1 or > MaximumPageSize)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.PageSizeOutOfRange);
        }

        if ((long)(requestedPage - 1) * requestedPageSize > int.MaxValue)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.PageOutOfRange);
        }

        if (query is not null && query.Length > SearchQueryMaximumLength)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.SearchQueryTooLong);
        }

        Result<WatchedInstrumentPage> result = await handler.HandleAsync(
            new SearchWatchedInstruments(query, requestedPage, requestedPageSize),
            cancellationToken);
        if (result.IsFailure)
        {
            return ApiProblemDetails.Problem(result.Error);
        }

        WatchedInstrumentPage found = result.Value;

        return TypedResults.Ok(new WatchedInstrumentPageResponse(
            found.Items.Select(ToSummaryDto).ToArray(),
            found.Page,
            found.PageSize,
            found.TotalCount));
    }

    private static async Task<IResult> GetSettingsAsync(
        Guid id,
        GetWatchedInstrumentSettingsHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<WatchedInstrumentSettingsSnapshot> settings = await handler.HandleAsync(
            new GetWatchedInstrumentSettings(id),
            cancellationToken);
        if (settings.IsFailure)
        {
            return ApiProblemDetails.Problem(settings.Error);
        }

        ConcurrencyTokens.SetConcurrencyHeaders(context, settings.Value.ConcurrencyToken);

        return TypedResults.Ok(ToSettingsResponse(settings.Value));
    }

    private static async Task<IResult> PutSettingsAsync(
        Guid id,
        UpdateWatchedInstrumentSettingsRequest? request,
        UpdateWatchedInstrumentSettingsHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ApiProblemDetails.Problem(WatchedInstrumentErrors.RequestRequired);
        }

        Result<MonitoringState> monitoringState = MapMonitoringState(request.MonitoringState);
        if (monitoringState.IsFailure)
        {
            return ApiProblemDetails.Problem(monitoringState.Error);
        }

        (byte[]? token, Error? tokenError) = ConcurrencyTokens.ExpectedToken(
            context.Request,
            WatchedInstrumentErrors.ConcurrencyTokenInvalid);
        if (tokenError is not null)
        {
            return ApiProblemDetails.Problem(tokenError);
        }

        Result<WatchedInstrumentSettingsSnapshot> saved = await handler.HandleAsync(
            new UpdateWatchedInstrumentSettings(
                id,
                monitoringState.Value,
                request.SamplingIntervalSeconds,
                token),
            cancellationToken);
        if (saved.IsFailure)
        {
            return ApiProblemDetails.Problem(saved.Error);
        }

        ConcurrencyTokens.SetConcurrencyHeaders(context, saved.Value.ConcurrencyToken);

        return TypedResults.Ok(ToSettingsResponse(saved.Value));
    }

    private static Result<MonitoringState> MapMonitoringState(string? value)
    {
        return value switch
        {
            "configured" => MonitoringState.Configured,
            "monitored" => MonitoringState.Monitored,
            _ => WatchedInstrumentErrors.MonitoringStateUndefined
        };
    }

    private static WatchedInstrumentResponse ToResponse(
        WatchedInstrument instrument,
        ChartAnalysisDefinition definition)
    {
        return new WatchedInstrumentResponse(
            instrument.Id,
            instrument.Symbol,
            instrument.Exchange,
            instrument.QuoteCurrency,
            FormatMonitoringState(instrument.MonitoringState),
            instrument.SamplingIntervalSeconds,
            ToOffset(instrument.CreatedAt),
            ToOffset(instrument.LastChangedAt),
            definition.PriceScale,
            ChartAnalysisMapping.ToDto(definition).SupportZones!,
            ChartAnalysisMapping.ToDto(definition).ResistanceZones!);
    }

    private static WatchedInstrumentSummaryDto ToSummaryDto(WatchedInstrumentSummary summary)
    {
        return new WatchedInstrumentSummaryDto(
            summary.Id,
            summary.Symbol,
            summary.Exchange,
            summary.QuoteCurrency,
            FormatMonitoringState(summary.MonitoringState),
            summary.SamplingIntervalSeconds);
    }

    private static WatchedInstrumentSettingsResponse ToSettingsResponse(
        WatchedInstrumentSettingsSnapshot snapshot)
    {
        WatchedInstrument instrument = snapshot.Instrument;

        return new WatchedInstrumentSettingsResponse(
            instrument.Id,
            instrument.Symbol,
            instrument.Exchange,
            instrument.QuoteCurrency,
            FormatMonitoringState(instrument.MonitoringState),
            instrument.SamplingIntervalSeconds,
            ToOffset(instrument.CreatedAt),
            ToOffset(instrument.LastChangedAt),
            ConcurrencyTokens.FormatToken(snapshot.ConcurrencyToken));
    }

    private static string FormatMonitoringState(MonitoringState state)
    {
        return state switch
        {
            MonitoringState.Configured => "configured",
            MonitoringState.Monitored => "monitored",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown monitoring state.")
        };
    }

    private static DateTimeOffset ToOffset(Instant instant)
    {
        return new DateTimeOffset(instant.ToDateTimeUtc());
    }
}
