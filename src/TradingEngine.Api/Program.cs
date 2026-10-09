using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TradingEngine.Api;
using TradingEngine.Api.Authentication;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Api.MonitoringRules;
using TradingEngine.Api.RateLimiting;
using TradingEngine.Api.WatchedInstruments;
using TradingEngine.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string? connectionString = builder.Configuration.GetConnectionString("TradingEngine");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:TradingEngine is not configured. For local development set the " +
        "passwordless Azure Dev connection string via 'dotnet user-secrets' or the " +
        "ConnectionStrings__TradingEngine environment variable on the Rider run configuration " +
        "(see docs/rider-local-azure-dev.md). Deployed instances receive it from the App " +
        "Service ConnectionStrings__TradingEngine application setting.");
}

builder.Services
    .AddApiDiagnostics(builder.Configuration, builder.Environment.EnvironmentName, connectionString)
    .AddEasyAuth(builder.Configuration)
    .AddApiRateLimiting(builder.Configuration)
    .AddSystemClock()
    .AddWatchedInstruments()
    .AddMonitoringRules()
    .AddTradingEngineInfrastructure(connectionString);

WebApplication app = builder.Build();

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/health/database"),
    branch => branch
        .UseMiddleware<DatabaseProbeKeyMiddleware>()
        .UseMiddleware<RateLimitingGateMiddleware>(RateLimitingGate.DatabaseProbe));

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    branch => branch
        .UseMiddleware<EasyAuthPrincipalMiddleware>()
        .UseMiddleware<RateLimitingGateMiddleware>(RateLimitingGate.Administration));

app.MapHealthChecks(
        "/health",
        new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("database") is false,
            ResponseWriter = HealthResponseWriter.WriteAsync
        })
    .AllowAnonymous();

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

app.MapWatchedInstrumentEndpoints();
app.MapMonitoringRuleEndpoints();

app.Run();
