using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NodaTime;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Api.WatchedInstruments;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WatchedInstruments.Read;
using TradingEngine.Application.WatchedInstruments.Register;
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
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddScoped<RegisterWatchedInstrumentHandler>(provider =>
    new RegisterWatchedInstrumentHandler(
        provider.GetRequiredService<IClock>(),
        provider.GetRequiredService<IWatchedInstrumentStore>()));
builder.Services.AddScoped<GetWatchedInstrumentHandler>(provider =>
    new GetWatchedInstrumentHandler(provider.GetRequiredService<IWatchedInstrumentStore>()));

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

app.MapWatchedInstrumentEndpoints();

app.Run();
