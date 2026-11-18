using TradingEngine.Application.Signals;

namespace TradingEngine.Api.Signals;

internal static class SignalServiceCollectionExtensions
{
    public static IServiceCollection AddSignals(this IServiceCollection services)
    {
        services.AddScoped<ReadSignalsHandler>();
        services.AddSingleton<SignalFreshnessClassifier>();
        return services;
    }
}
