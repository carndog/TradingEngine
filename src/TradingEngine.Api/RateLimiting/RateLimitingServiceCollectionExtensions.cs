using System.Threading.RateLimiting;
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
        services.AddSingleton(provider =>
        {
            ApiRateLimitOptions limits = provider
                .GetRequiredService<IOptions<ApiRateLimitOptions>>()
                .Value;

            return new ConcurrencyLimiter(
                new ConcurrencyLimiterOptions
                {
                    PermitLimit = limits.ConcurrencyPermitLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = limits.QueueLimit
                });
        });
        services.AddSingleton<IAdministrationRequestLimiter, AdministrationRequestLimiter>();
        services.AddSingleton<IDatabaseProbeRequestLimiter, DatabaseProbeRequestLimiter>();

        return services;
    }
}
