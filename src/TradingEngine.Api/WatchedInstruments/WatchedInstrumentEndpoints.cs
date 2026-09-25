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
            return Problem(WatchedInstrumentErrors.RequestRequired);
        }

        Result<ChartAnalysisDefinition> definition = MapDefinition(request);
        if (definition.IsFailure)
        {
            return Problem(definition.Error);
        }

        Result<MonitoringState> monitoringState = MapMonitoringState(request.MonitoringState);
        if (monitoringState.IsFailure)
        {
            return Problem(monitoringState.Error);
        }

        RegisterWatchedInstrument command = new(
            Guid.NewGuid(),
            request.Symbol ?? string.Empty,
            request.Exchange ?? string.Empty,
            request.QuoteCurrency ?? string.Empty,
            request.SamplingIntervalSeconds,
            monitoringState.Value,
            definition.Value);

        Result<WatchedInstrument> registered = await handler.HandleAsync(command, cancellationToken);
        if (registered.IsFailure)
        {
            return Problem(registered.Error);
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
            return Problem(configuration.Error);
        }

        return TypedResults.Ok(ToResponse(
            configuration.Value.Instrument,
            configuration.Value.Definition));
    }

    private static ProblemHttpResult Problem(Error error)
    {
        int statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };

        return TypedResults.Problem(
            statusCode: statusCode,
            detail: error.Description,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code
            });
    }

    private static Result<ChartAnalysisDefinition> MapDefinition(RegisterWatchedInstrumentRequest request)
    {
        Result<ChartZone[]> supportZones = MapZones(request.SupportZones);
        if (supportZones.IsFailure)
        {
            return supportZones.Error;
        }

        Result<ChartZone[]> resistanceZones = MapZones(request.ResistanceZones);
        if (resistanceZones.IsFailure)
        {
            return resistanceZones.Error;
        }

        return ChartAnalysisDefinition.Create(
            request.PriceScale,
            supportZones.Value,
            resistanceZones.Value);
    }

    private static Result<ChartZone[]> MapZones(IReadOnlyList<ChartZoneDto>? zones)
    {
        if (zones is null)
        {
            return Result<ChartZone[]>.Success([]);
        }

        List<ChartZone> mapped = new(zones.Count);
        foreach (ChartZoneDto zone in zones)
        {
            Result<ChartZone> result = MapZone(zone);
            if (result.IsFailure)
            {
                return result.Error;
            }

            mapped.Add(result.Value);
        }

        return mapped.ToArray();
    }

    private static Result<ChartZone> MapZone(ChartZoneDto zone)
    {
        if (zone is null)
        {
            return ChartAnalysisErrors.ZoneRequired;
        }

        Result<ChartAnalysisIdentifier> id = ChartAnalysisIdentifier.From(zone.Id);
        if (id.IsFailure)
        {
            return id.Error;
        }

        Result<ChartCondition[]> conditions = MapConditions(zone.Conditions);
        if (conditions.IsFailure)
        {
            return conditions.Error;
        }

        return ChartZone.Create(id.Value, zone.Lower, zone.Level, zone.Upper, conditions.Value);
    }

    private static Result<ChartCondition[]> MapConditions(IReadOnlyList<ChartConditionDto>? conditions)
    {
        if (conditions is null)
        {
            return Result<ChartCondition[]>.Success([]);
        }

        List<ChartCondition> mapped = new(conditions.Count);
        foreach (ChartConditionDto condition in conditions)
        {
            Result<ChartCondition> result = MapCondition(condition);
            if (result.IsFailure)
            {
                return result.Error;
            }

            mapped.Add(result.Value);
        }

        return mapped.ToArray();
    }

    private static Result<ChartCondition> MapCondition(ChartConditionDto condition)
    {
        if (condition is null)
        {
            return ChartAnalysisErrors.ConditionRequired;
        }

        Result<ChartConditionType> type = MapConditionType(condition.Type);
        if (type.IsFailure)
        {
            return type.Error;
        }

        Result<ChartAnalysisIdentifier> actionId = ChartAnalysisIdentifier.From(condition.ActionId);
        if (actionId.IsFailure)
        {
            return actionId.Error;
        }

        return ChartCondition.Create(type.Value, actionId.Value);
    }

    private static Result<ChartConditionType> MapConditionType(string? value)
    {
        return value switch
        {
            "buy-zone" => ChartConditionType.BuyZone,
            "support-loss" => ChartConditionType.SupportLoss,
            "breakout" => ChartConditionType.Breakout,
            _ => ChartAnalysisErrors.ConditionTypeUndefined
        };
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
            definition.SupportZones.Select(ToDto).ToArray(),
            definition.ResistanceZones.Select(ToDto).ToArray());
    }

    private static ChartZoneDto ToDto(ChartZone zone)
    {
        return new ChartZoneDto(
            zone.Id.Value,
            zone.Lower,
            zone.Level,
            zone.Upper,
            zone.Conditions.Select(ToDto).ToArray());
    }

    private static ChartConditionDto ToDto(ChartCondition condition)
    {
        return new ChartConditionDto(
            FormatConditionType(condition.Type),
            condition.ActionId.Value);
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

    private static string FormatConditionType(ChartConditionType type)
    {
        return type switch
        {
            ChartConditionType.BuyZone => "buy-zone",
            ChartConditionType.SupportLoss => "support-loss",
            ChartConditionType.Breakout => "breakout",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown chart condition type.")
        };
    }

    private static DateTimeOffset ToOffset(Instant instant)
    {
        return new DateTimeOffset(instant.ToDateTimeUtc());
    }
}
