using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments.Delete;
using TradingEngine.Application.WatchedInstruments.Read;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Application.WatchedInstruments.Search;
using TradingEngine.Application.WatchedInstruments.Settings;

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
        services.AddScoped<SearchWatchedInstrumentsHandler>(provider =>
            new SearchWatchedInstrumentsHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>()));
        services.AddScoped<GetWatchedInstrumentSettingsHandler>(provider =>
            new GetWatchedInstrumentSettingsHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>()));
        services.AddScoped<UpdateWatchedInstrumentSettingsHandler>(provider =>
            new UpdateWatchedInstrumentSettingsHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<BulkDeleteWatchedInstrumentsHandler>(provider =>
            new BulkDeleteWatchedInstrumentsHandler(
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<IWatchedInstrumentStore>()));

        return services;
    }
}
