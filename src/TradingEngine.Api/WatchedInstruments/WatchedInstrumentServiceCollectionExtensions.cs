using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments.Read;
using TradingEngine.Application.WatchedInstruments.Register;

namespace TradingEngine.Api.WatchedInstruments;

internal static class WatchedInstrumentServiceCollectionExtensions
{
    public static IServiceCollection AddWatchedInstruments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<RegisterWatchedInstrumentHandler>(provider =>
            new RegisterWatchedInstrumentHandler(
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<IWatchedInstrumentStore>()));
        services.AddScoped<GetWatchedInstrumentHandler>(provider =>
            new GetWatchedInstrumentHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>(),
                provider.GetRequiredService<IClock>()));

        return services;
    }
}
