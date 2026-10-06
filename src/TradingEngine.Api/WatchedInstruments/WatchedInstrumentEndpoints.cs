using Microsoft.AspNetCore.Http.HttpResults;
using NodaTime;
using TradingEngine.Application.WatchedInstruments;
using TradingEngine.Application.WatchedInstruments.Read;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Instruments;
using TradingEngine.Domain.MonitoringRules;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.WatchedInstruments;

internal static class WatchedInstrumentEndpoints
{
    private const string GroupPath = "/api/watched-instruments";

    public static RouteGroupBuilder MapWatchedInstrumentEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup(GroupPath);
        group.MapPost("", RegisterAsync);
        group.MapGet("/{id:guid}", GetAsync);

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
