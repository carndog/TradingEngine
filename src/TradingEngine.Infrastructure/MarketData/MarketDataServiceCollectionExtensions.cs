using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NodaTime;
using TradingEngine.Application.Ports;

namespace TradingEngine.Infrastructure.MarketData;

public static class MarketDataServiceCollectionExtensions
{
    public static IServiceCollection AddEtoroMarketData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EToroMarketDataOptions>()
            .Bind(configuration.GetSection(EToroMarketDataOptions.SectionName))
            .PostConfigure(options => options.NormalizeMappings());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<ProviderRateGate>();
        services.AddSingleton<EToroInstrumentIdCache>();
        services.AddHttpClient<IMarketDataProvider, EToroMarketDataProvider>(
            (provider, client) =>
            {
                EToroMarketDataOptions options = provider
                    .GetRequiredService<IOptions<EToroMarketDataOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    options.BaseUrl.TrimEnd('/') + "/",
                    UriKind.Absolute);
                client.Timeout = Timeout.InfiniteTimeSpan;
            });

        return services;
    }
}
