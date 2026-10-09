using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TradingEngine.Api.RateLimiting;
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

        string? connectionString = configuration[ConnectionStringConfigurationKey];

        if (string.IsNullOrWhiteSpace(connectionString) is false)
        {
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

            services.ConfigureOpenTelemetryLoggerProvider((_, loggerProviderBuilder) =>
                loggerProviderBuilder
                    .AddProcessor(new TelemetryEnrichmentLogProcessor(identity, environmentName))
                    .AddProcessor(new SensitiveDataLogProcessor()));

            services.ConfigureOpenTelemetryMeterProvider((_, meterProviderBuilder) =>
                meterProviderBuilder.AddMeter(RateLimitingTelemetry.MeterName));

            services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
                options.RecordException = false);
            services.Configure<HttpClientTraceInstrumentationOptions>(options =>
                options.RecordException = false);
        }

        services.AddHostedService<StartupTelemetryHostedService>();

        return services;
    }
}
