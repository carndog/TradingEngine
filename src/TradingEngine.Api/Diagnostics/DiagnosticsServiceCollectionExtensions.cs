using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TradingEngine.Api.Diagnostics;

internal static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddApiDiagnostics(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        services.AddApiTelemetry(configuration, environmentName);
        services.AddSingleton<ApplicationVersionProvider>();
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                "database",
                _ => new DatabaseReadinessHealthCheck(connectionString),
                failureStatus: null,
                tags: ["database"]));

        return services;
    }
}
