using NodaTime;
using NodaTime.Text;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Time;
using TradingEngine.Application.WeeklyReports;
using TradingEngine.Domain.Results;

namespace TradingEngine.Api.WeeklyReports;

internal static class WeeklyReportEndpoints
{
    private static Error InvalidQuery => Error.Validation("weekly_reports.invalid_query", "Supply a past UTC cutoff and a bounded page (1–100 rows).");

    public static void MapWeeklyReportEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/weekly-reports");
        group.MapGet("", ListAsync);
        group.MapGet("/preview", PreviewAsync);
        group.MapGet("/{id:guid}", GetAsync);
    }

    private static async Task<IResult> ListAsync(int? maximum, IWeeklyReportStore store,
        WeeklyReportOptions options, CancellationToken cancellationToken)
    {
        int count = maximum ?? 25;
        return count is < 1 or > 100 ? ApiProblemDetails.Problem(InvalidQuery)
            : TypedResults.Ok(await store.ListAsync(options.SubscriptionKey, count, cancellationToken));
    }

    private static async Task<IResult> GetAsync(Guid id, int? page, int? pageSize, IWeeklyReportStore store,
        WeeklyReportOptions options, CancellationToken cancellationToken)
    {
        if (ValidPage(page ?? 1, pageSize ?? 25) is false)
        {
            return ApiProblemDetails.Problem(InvalidQuery);
        }

        WeeklyReportPage? report = await store.ReadAsync(options.SubscriptionKey, id, page ?? 1, pageSize ?? 25, cancellationToken);
        return report is null ? TypedResults.NotFound() : TypedResults.Ok(report);
    }

    private static async Task<IResult> PreviewAsync(string? cutoff, int? page, int? pageSize, WeeklyReportComposer composer,
        WeeklyReportOptions options, IClock clock, CancellationToken cancellationToken)
    {
        Instant at = clock.GetCurrentInstant();
        if (cutoff is not null)
        {
            ParseResult<Instant> parsed = InstantPattern.ExtendedIso.Parse(cutoff);
            if (parsed.Success is false)
            {
                ParseResult<OffsetDateTime> offset = OffsetDateTimePattern.ExtendedIso.Parse(cutoff);
                if (offset.Success is false || offset.Value.Offset != Offset.Zero)
                {
                    return ApiProblemDetails.Problem(InvalidQuery);
                }

                at = offset.Value.ToInstant();
            }
            else
            {
                at = parsed.Value;
            }
        }

        int number = page ?? 1;
        int size = pageSize ?? 25;
        if (ValidPage(number, size) is false || PersistedInstant.IsPersistable(at) is false
            || at > clock.GetCurrentInstant() || at < Instant.FromUtc(2000, 1, 8, 0, 0))
        {
            return ApiProblemDetails.Problem(InvalidQuery);
        }

        DateTimeZone zone = DateTimeZoneProviders.Tzdb[options.TimeZone];
        LocalDateTime local = at.InZone(zone).LocalDateTime;
        WeeklyReportPeriod period = new(zone.AtLeniently(local.PlusDays(-7)).ToInstant(), at,
            zone.AtLeniently(local.PlusDays(7)).ToInstant());
        WeeklyReportSnapshot snapshot = await composer.ComposeAsync(period, null, [], 0, cancellationToken);
        return TypedResults.Ok(new WeeklyReportPreview(period.StartsAt.ToDateTimeOffset(), at.ToDateTimeOffset(), snapshot.Items.Count,
            snapshot.Items.Skip((number - 1) * size).Take(size).ToArray(), number, size, (long)number * size < snapshot.Items.Count,
            snapshot.Email.Subject, snapshot.Email.PlainTextBody, snapshot.Email.HtmlBody));
    }

    private static bool ValidPage(int page, int size)
    {
        return page >= 1 && size is >= 1 and <= 100 && (long)(page - 1) * size <= int.MaxValue;
    }
}
