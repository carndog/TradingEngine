using NodaTime;
using TradingEngine.Application.Listings;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;
using TradingEngine.Application.WatchedInstruments.Delete;
using TradingEngine.Application.WatchedInstruments.Read;
using TradingEngine.Application.WatchedInstruments.Register;
using TradingEngine.Application.WatchedInstruments.Search;
using TradingEngine.Application.WatchedInstruments.Settings;
using TradingEngine.Application.WatchedInstruments.Status;

namespace TradingEngine.Api.WatchedInstruments;

internal static class WatchedInstrumentServiceCollectionExtensions
{
    public static IServiceCollection AddWatchedInstruments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ListingStatusEvaluator>(provider =>
            new ListingStatusEvaluator(
                provider.GetRequiredService<INativeQuoteUnitMap>()));
        services.AddScoped<MonitoringReadinessEvaluator>(provider =>
            new MonitoringReadinessEvaluator(
                provider.GetRequiredService<INativeQuoteUnitMap>(),
                provider.GetRequiredService<IMarketSessionPolicy>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<CollectionStatusEvaluator>(provider =>
            new CollectionStatusEvaluator(
                provider.GetRequiredService<INativeQuoteUnitMap>(),
                provider.GetRequiredService<IMarketSessionPolicy>(),
                provider.GetRequiredService<IPriceObservationStore>(),
                provider.GetRequiredService<IPriceObservationHistoryReader>(),
                provider.GetRequiredService<PriceCollectionOptions>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<RegisterWatchedInstrumentHandler>(provider =>
            new RegisterWatchedInstrumentHandler(
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<IWatchedInstrumentStore>(),
                provider.GetRequiredService<IMarketDataProvider>(),
                provider.GetRequiredService<IListingConfigurationStore>(),
                provider.GetRequiredService<INativeQuoteUnitMap>(),
                provider.GetRequiredService<MonitoringReadinessEvaluator>()));
        services.AddScoped<GetWatchedInstrumentHandler>(provider =>
            new GetWatchedInstrumentHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<SearchWatchedInstrumentsHandler>(provider =>
            new SearchWatchedInstrumentsHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>()));
        services.AddScoped<GetWatchedInstrumentSettingsHandler>(provider =>
            new GetWatchedInstrumentSettingsHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>(),
                provider.GetRequiredService<ListingStatusEvaluator>(),
                provider.GetRequiredService<CollectionStatusEvaluator>()));
        services.AddScoped<UpdateWatchedInstrumentSettingsHandler>(provider =>
            new UpdateWatchedInstrumentSettingsHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<MonitoringReadinessEvaluator>(),
                provider.GetRequiredService<ListingStatusEvaluator>(),
                provider.GetRequiredService<CollectionStatusEvaluator>()));
        services.AddScoped<ConfirmListingUnitHandler>(provider =>
            new ConfirmListingUnitHandler(
                provider.GetRequiredService<IWatchedInstrumentStore>(),
                provider.GetRequiredService<IListingConfigurationStore>(),
                provider.GetRequiredService<IMarketDataProvider>(),
                provider.GetRequiredService<INativeQuoteUnitMap>(),
                provider.GetRequiredService<ListingStatusEvaluator>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<BulkDeleteWatchedInstrumentsHandler>(provider =>
            new BulkDeleteWatchedInstrumentsHandler(
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<IWatchedInstrumentStore>()));

        return services;
    }
}
