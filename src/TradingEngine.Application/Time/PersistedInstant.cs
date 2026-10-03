using NodaTime;

namespace TradingEngine.Application.Time;

public static class PersistedInstant
{
    private static readonly Instant Minimum =
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));

    private static readonly Instant Maximum =
        Instant.FromDateTimeUtc(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));

    public static Instant Require(Instant instant, string parameterName)
    {
        if (instant < Minimum || instant > Maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                instant,
                "The instant is outside the range SQL Server datetime2(7) can store.");
        }

        if (instant != Instant.FromUnixTimeTicks(instant.ToUnixTimeTicks()))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                instant,
                "The instant has precision finer than SQL Server datetime2(7) ticks (100 ns).");
        }

        return instant;
    }

    public static Instant? Require(Instant? instant, string parameterName)
    {
        return instant is null ? null : Require(instant.Value, parameterName);
    }
}
