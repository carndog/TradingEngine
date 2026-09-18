using System.Linq.Expressions;
using NodaTime;

namespace TradingEngine.Infrastructure.Persistence;

internal static class InstantConverters
{
    public static readonly Expression<Func<Instant, DateTime>> ToUtcDateTime =
        instant => instant.ToDateTimeUtc();

    public static readonly Expression<Func<DateTime, Instant>> FromUtcDateTime =
        value => Instant.FromDateTimeUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
