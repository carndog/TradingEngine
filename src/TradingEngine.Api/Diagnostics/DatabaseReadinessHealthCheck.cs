using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TradingEngine.Api.Diagnostics;

internal sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    private const int ConnectTimeoutSeconds = 5;

    private readonly string? _connectionString;

    public DatabaseReadinessHealthCheck(string? connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return HealthCheckResult.Unhealthy("The database connection is not configured.");
        }

        SqlConnectionStringBuilder builder = new(_connectionString)
        {
            ConnectTimeout = ConnectTimeoutSeconds
        };

        try
        {
            await using SqlConnection connection = new(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = new("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database readiness check failed.");
        }
    }
}
