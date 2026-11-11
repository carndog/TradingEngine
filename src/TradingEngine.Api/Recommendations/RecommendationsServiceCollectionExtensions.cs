using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Recommendations;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Api.Recommendations;

internal static class RecommendationsServiceCollectionExtensions
{
    public static IServiceCollection AddRecommendations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        RecommendationOptions options = configuration
            .GetSection(RecommendationOptions.SectionName)
            .Get<RecommendationOptions>() ?? new RecommendationOptions();

        services.AddSingleton(options);
        services.TryAddSingleton<RecommendationTelemetry>();

        if (options.Enabled is false)
        {
            return services;
        }

        if (options.PollIntervalSeconds < 1)
        {
            throw new InvalidOperationException(
                "Recommendations:PollIntervalSeconds must be at least 1 when the " +
                "evaluator is enabled.");
        }

        if (options.MaxObservationsPerInstrument < 1)
        {
            throw new InvalidOperationException(
                "Recommendations:MaxObservationsPerInstrument must be at least 1 when " +
                "the evaluator is enabled.");
        }

        if (options.MaxExecutionTimeSeconds < 1)
        {
            throw new InvalidOperationException(
                "Recommendations:MaxExecutionTimeSeconds must be at least 1 when the " +
                "evaluator is enabled.");
        }

        if (Enum.TryParse(options.PriceBasis, true, out PriceBasis basis) is false
            || Enum.IsDefined(basis) is false)
        {
            throw new InvalidOperationException(
                "Recommendations:PriceBasis must be 'Bid' or 'Ask' when the evaluator " +
                "is enabled.");
        }

        services.AddSingleton(new ChartAnalysisEvaluator());
        services.AddScoped<EvaluateSavedPricesHandler>(provider =>
            new EvaluateSavedPricesHandler(
                provider.GetRequiredService<IRecommendationEvaluationStore>(),
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<ChartAnalysisEvaluator>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<RecommendationOptions>()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService(provider =>
            new RecommendationEvaluationHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<RecommendationEvaluationHostedService>>(),
                provider.GetRequiredService<RecommendationTelemetry>(),
                options.PollIntervalSeconds));

        return services;
    }
}
