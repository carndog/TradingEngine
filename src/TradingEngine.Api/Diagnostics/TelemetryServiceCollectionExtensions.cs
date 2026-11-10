using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
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
    internal const string OtlpEndpointConfigurationKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    private const string OtlpExporterHttpClientLogCategory = "System.Net.Http.HttpClient.Otlp";

    public static IServiceCollection AddApiTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        TelemetryExportTarget target = SelectExportTarget(configuration, environmentName);

        if (target == TelemetryExportTarget.AzureMonitor)
        {
            AddAzureMonitorExport(services, configuration[ConnectionStringConfigurationKey]!, environmentName);
        }
        else if (target == TelemetryExportTarget.LocalOtlp)
        {
            AddLocalOtlpExport(services, environmentName);
        }

        services.AddHostedService<StartupTelemetryHostedService>();

        return services;
    }

    internal static TelemetryExportTarget SelectExportTarget(
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        bool isDevelopment = string.Equals(
            environmentName,
            Environments.Development,
            StringComparison.OrdinalIgnoreCase);

        if (isDevelopment && string.IsNullOrWhiteSpace(configuration[OtlpEndpointConfigurationKey]) is false)
        {
            return TelemetryExportTarget.LocalOtlp;
        }

        if (string.IsNullOrWhiteSpace(configuration[ConnectionStringConfigurationKey]) is false)
        {
            return TelemetryExportTarget.AzureMonitor;
        }

        return TelemetryExportTarget.None;
    }

    private static void AddAzureMonitorExport(
        IServiceCollection services,
        string connectionString,
        string environmentName)
    {
        VersionResponse identity = new ApplicationVersionProvider().GetCurrent();

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => ConfigureResource(resource, identity, environmentName))
            .UseAzureMonitor(options =>
            {
                options.ConnectionString = connectionString;
                options.SamplingRatio = 1.0F;
                options.EnableLiveMetrics = true;
            });

        AddSharedPipeline(services, identity, environmentName);
    }

    private static void AddLocalOtlpExport(IServiceCollection services, string environmentName)
    {
        VersionResponse identity = new ApplicationVersionProvider().GetCurrent();

        AddSharedPipeline(services, identity, environmentName);

        services.AddLogging(logging =>
            logging.AddFilter(OtlpExporterHttpClientLogCategory, LogLevel.Warning));

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => ConfigureResource(resource, identity, environmentName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithLogging(
                _ => { },
                options => options.IncludeFormattedMessage = true)
            .UseOtlpExporter();
    }

    private static void ConfigureResource(
        ResourceBuilder resource,
        VersionResponse identity,
        string environmentName)
    {
        resource
            .AddService(identity.Application, serviceVersion: identity.Version)
            .AddAttributes(
                new Dictionary<string, object>
                {
                    [TelemetryEnrichmentProcessor.EnvironmentTag] = environmentName
                });
    }

    private static void AddSharedPipeline(
        IServiceCollection services,
        VersionResponse identity,
        string environmentName)
    {
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
}
