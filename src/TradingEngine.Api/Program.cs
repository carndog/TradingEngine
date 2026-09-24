using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string? connectionString = builder.Configuration.GetConnectionString("TradingEngine");

builder.Services.AddHealthChecks()
    .Add(new HealthCheckRegistration(
        "database",
        _ => new DatabaseReadinessHealthCheck(connectionString),
        failureStatus: null,
        tags: ["database"]));
builder.Services.AddSingleton<ApplicationVersionProvider>();

if (string.IsNullOrWhiteSpace(connectionString) is false)
{
    builder.Services.AddTradingEngineInfrastructure(connectionString);
}

WebApplication app = builder.Build();

app.MapHealthChecks(
        "/health",
        new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("database") is false,
            ResponseWriter = HealthResponseWriter.WriteAsync
        })
    .AllowAnonymous();

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/health/database"),
    branch => branch.UseMiddleware<DatabaseProbeKeyMiddleware>());

app.MapHealthChecks(
        "/health/database",
        new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("database"),
            ResponseWriter = HealthResponseWriter.WriteAsync
        })
    .AllowAnonymous();

app.MapGet(
        "/version",
        (ApplicationVersionProvider versionProvider) => TypedResults.Ok(versionProvider.GetCurrent()))
    .AllowAnonymous();

app.MapGet("/auth-check", () => TypedResults.NoContent());

app.Run();
