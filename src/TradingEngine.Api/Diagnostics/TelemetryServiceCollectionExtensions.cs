using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.Diagnostics;

internal static class TelemetryServiceCollectionExtensions
{
    internal const string ConnectionStringConfigurationKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static IServiceCollection AddApiTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        services.AddHostedService<StartupTelemetryHostedService>();

        string? connectionString = configuration[ConnectionStringConfigurationKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        VersionResponse identity = new ApplicationVersionProvider().GetCurrent();

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(identity.Application, serviceVersion: identity.Version)
                .AddAttributes(
                    new Dictionary<string, object>
                    {
                        [TelemetryEnrichmentProcessor.EnvironmentTag] = environmentName
                    }))
            .UseAzureMonitor(options =>
            {
                options.ConnectionString = connectionString;
                options.SamplingRatio = 1.0F;
                options.EnableLiveMetrics = true;
            });

        services.ConfigureOpenTelemetryTracerProvider((_, tracerProviderBuilder) =>
            tracerProviderBuilder
                .AddProcessor(new TelemetryEnrichmentProcessor(identity, environmentName))
                .AddProcessor(new SensitiveDataTelemetryProcessor()));

        return services;
    }
}
