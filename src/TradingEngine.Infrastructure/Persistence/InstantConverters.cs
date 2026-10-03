using System.Linq.Expressions;
using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal static class InstantConverters
{
    public static readonly Expression<Func<Instant, DateTime>> ToUtcDateTime =
        instant => instant.ToDateTimeUtc();

    public static readonly Expression<Func<DateTime, Instant>> FromUtcDateTime =
        value => Instant.FromDateTimeUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static readonly Expression<Func<Instant?, DateTime?>> ToUtcDateTimeNullable =
        instant => instant.HasValue ? instant.Value.ToDateTimeUtc() : null;

    public static readonly Expression<Func<DateTime?, Instant?>> FromUtcDateTimeNullable =
        value => value.HasValue
            ? Instant.FromDateTimeUtc(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : null;
}
