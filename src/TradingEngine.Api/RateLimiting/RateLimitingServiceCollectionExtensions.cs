using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace TradingEngine.Api.RateLimiting;

internal static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<RateLimitingTelemetry>();
        services.AddOptions<ApiRateLimitOptions>()
            .Bind(configuration.GetSection(ApiRateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.WritePermitLimit <= options.ReadPermitLimit,
                "RateLimiting:WritePermitLimit must not exceed RateLimiting:ReadPermitLimit.")
            .ValidateOnStart();
        services.AddSingleton<IConcurrencyLimiterFactory, ConcurrencyLimiterFactory>();
        services.AddSingleton<IRequestBudgetLimiter, RequestBudgetLimiter>();
        services.AddSingleton<IConfigureOptions<RateLimiterOptions>, ApiRateLimiterOptionsSetup>();
        services.AddRateLimiter();

        return services;
    }
}
