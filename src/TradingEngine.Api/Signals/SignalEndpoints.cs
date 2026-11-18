using NodaTime;
using NodaTime.Text;
using TradingEngine.Application.Signals;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.Signals;

internal static class SignalEndpoints
{
    public static void MapSignalEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/signals");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
    }

    private static async Task<IResult> ListAsync(
        Guid? instrumentId, string? cutoff, string? triggeredFrom, string? triggeredTo,
        int? page, int? pageSize, ReadSignalsHandler handler, IClock clock, CancellationToken cancellationToken)
    {
        if (TryTime(cutoff, out Instant? at) is false
            || TryTime(triggeredFrom, out Instant? from) is false
            || TryTime(triggeredTo, out Instant? to) is false)
        {
            return ApiProblemDetails.Problem(ReadSignalsHandler.InvalidQuery);
        }

        Result<SignalPage> result = await handler.HandleAsync(
            new SignalQuery(at ?? clock.GetCurrentInstant(), instrumentId, from, to, page ?? 1, pageSize ?? 25), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ApiProblemDetails.Problem(result.Error);
    }

    private static async Task<IResult> GetAsync(
        Guid id, string? cutoff, ReadSignalsHandler handler, IClock clock, CancellationToken cancellationToken)
    {
        if (TryTime(cutoff, out Instant? at) is false)
        {
            return ApiProblemDetails.Problem(ReadSignalsHandler.InvalidQuery);
        }

        Result<SignalPage> result = await handler.HandleAsync(
            new SignalQuery(at ?? clock.GetCurrentInstant(), PageSize: 1, SignalId: id), cancellationToken);
        if (result.IsFailure)
        {
            return ApiProblemDetails.Problem(result.Error);
        }

        SignalStatus? signal = result.Value.Items.FirstOrDefault();
        return signal is null ? ApiProblemDetails.Problem(ReadSignalsHandler.NotFound) : TypedResults.Ok(signal);
    }

    private static bool TryTime(string? text, out Instant? value)
    {
        value = null;
        if (text is null)
        {
            return true;
        }

        ParseResult<Instant> parsed = InstantPattern.ExtendedIso.Parse(text);
        if (parsed.Success is false)
        {
            ParseResult<OffsetDateTime> offset = OffsetDateTimePattern.ExtendedIso.Parse(text);
            if (offset.Success is false || offset.Value.Offset != Offset.Zero)
            {
                return false;
            }

            value = offset.Value.ToInstant();
            return true;
        }

        value = parsed.Value;
        return true;
    }
}
