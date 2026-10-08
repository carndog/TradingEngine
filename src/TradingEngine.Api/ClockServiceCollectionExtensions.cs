using NodaTime;

namespace TradingEngine.Api;

internal static class ClockServiceCollectionExtensions
{
    public static IServiceCollection AddSystemClock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClock>(SystemClock.Instance);

        return services;
    }
}
