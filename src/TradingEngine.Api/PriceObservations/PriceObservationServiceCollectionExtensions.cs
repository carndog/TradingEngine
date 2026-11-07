using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceObservations.Read;

namespace TradingEngine.Api.PriceObservations;

internal static class PriceObservationServiceCollectionExtensions
{
    public static IServiceCollection AddPriceObservations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<GetPriceObservationHistoryHandler>(provider =>
            new GetPriceObservationHistoryHandler(
                provider.GetRequiredService<IPriceObservationHistoryReader>()));

        return services;
    }
}
