using System.Globalization;
using NodaTime;
using NodaTime.Text;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.PriceObservations;
using TradingEngine.Application.PriceObservations.Read;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.PriceObservations;

internal static class PriceObservationEndpoints
{
    private const string GroupPath = "/api/watched-instruments/{id:guid}/price-observations";

    private static readonly InstantPattern UtcOffsetCursorPattern =
        InstantPattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm':'ss;FFFFFFFFF'+00:00'");

    public static RouteGroupBuilder MapPriceObservationEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        RouteGroupBuilder group = routes.MapGroup(GroupPath);
        group.MapGet("", GetAsync);

        return group;
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        string? before,
        string? pageSize,
        GetPriceObservationHistoryHandler handler,
        CancellationToken cancellationToken)
    {
        Instant? cursor = null;
        if (before is not null)
        {
            ParseResult<Instant> parsed = ParseCursor(before);
            if (parsed.Success is false)
            {
                return ApiProblemDetails.Problem(PriceObservationErrors.CursorInvalid);
            }

            cursor = parsed.Value;
        }

        int size = GetPriceObservationHistoryHandler.DefaultPageSize;
        if (pageSize is not null
            && int.TryParse(pageSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) is false)
        {
            return ApiProblemDetails.Problem(PriceObservationErrors.PageSizeOutOfRange);
        }

        Result<PriceObservationHistory> history = await handler.HandleAsync(
            new GetPriceObservationHistory(
                id,
                cursor,
                size),
            cancellationToken);
        if (history.IsFailure)
        {
            return ApiProblemDetails.Problem(history.Error);
        }

        PriceObservationHistory found = history.Value;

        return TypedResults.Ok(new PriceObservationPageResponse(
            found.InstrumentId,
            found.Latest is null ? null : ToDto(found.Latest),
            found.Items.Select(ToDto).ToArray(),
            found.PageSize,
            ToOffset(found.Before),
            ToOffset(found.NextBefore)));
    }

    private static ParseResult<Instant> ParseCursor(string before)
    {
        ParseResult<Instant> parsed = InstantPattern.ExtendedIso.Parse(before);
        if (parsed.Success)
        {
            return parsed;
        }

        return UtcOffsetCursorPattern.Parse(before);
    }

    private static PriceObservationDto ToDto(PriceObservationRecord record)
    {
        return new PriceObservationDto(
            ToOffset(record.ScheduledFor),
            ToOffset(record.RetrievedAt),
            ToOffset(record.ProviderObservedAt),
            record.Bid,
            record.Ask,
            record.QuoteCurrency,
            record.Provider,
            FormatTimeliness(record.Timeliness));
    }

    private static string FormatTimeliness(MarketQuoteTimeliness timeliness)
    {
        return timeliness switch
        {
            MarketQuoteTimeliness.Unknown => "unknown",
            MarketQuoteTimeliness.Realtime => "realtime",
            MarketQuoteTimeliness.Delayed => "delayed",
            _ => throw new ArgumentOutOfRangeException(nameof(timeliness), timeliness, "Unknown quote timeliness.")
        };
    }

    private static DateTimeOffset ToOffset(Instant instant)
    {
        return new DateTimeOffset(instant.ToDateTimeUtc());
    }

    private static DateTimeOffset? ToOffset(Instant? instant)
    {
        return instant is null ? null : ToOffset(instant.Value);
    }
}
