using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NodaTime;
using TradingEngine.Api.Authentication;
using TradingEngine.Api.Diagnostics;
using TradingEngine.Api.MonitoringRules;
using TradingEngine.Api.WatchedInstruments;
using TradingEngine.Application.MonitoringRules.Drafts;
using TradingEngine.Application.MonitoringRules.Lifecycle;
using TradingEngine.Application.MonitoringRules.Read;
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
builder.Services.Configure<EasyAuthOptions>(
    builder.Configuration.GetSection(EasyAuthOptions.SectionName));
builder.Services.AddApiTelemetry(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddScoped<RegisterWatchedInstrumentHandler>(provider =>
    new RegisterWatchedInstrumentHandler(
        provider.GetRequiredService<IClock>(),
        provider.GetRequiredService<IWatchedInstrumentStore>()));
builder.Services.AddScoped<GetWatchedInstrumentHandler>(provider =>
    new GetWatchedInstrumentHandler(
        provider.GetRequiredService<IWatchedInstrumentStore>(),
        provider.GetRequiredService<IClock>()));
builder.Services.AddScoped<GetMonitoringRuleTimelineHandler>(provider =>
    new GetMonitoringRuleTimelineHandler(
        provider.GetRequiredService<IMonitoringRuleStore>()));
builder.Services.AddScoped<GetApplicableMonitoringRuleRevisionHandler>(provider =>
    new GetApplicableMonitoringRuleRevisionHandler(
        provider.GetRequiredService<IMonitoringRuleStore>(),
        provider.GetRequiredService<IClock>()));
builder.Services.AddScoped<CreateMonitoringRuleDraftHandler>(provider =>
    new CreateMonitoringRuleDraftHandler(
        provider.GetRequiredService<IMonitoringRuleStore>(),
        provider.GetRequiredService<IClock>()));
builder.Services.AddScoped<EditMonitoringRuleDraftHandler>(provider =>
    new EditMonitoringRuleDraftHandler(
        provider.GetRequiredService<IMonitoringRuleStore>(),
        provider.GetRequiredService<IClock>()));
builder.Services.AddScoped<DeleteMonitoringRuleDraftHandler>(provider =>
    new DeleteMonitoringRuleDraftHandler(
        provider.GetRequiredService<IMonitoringRuleStore>(),
        provider.GetRequiredService<IClock>()));
builder.Services.AddScoped<ApplyMonitoringRuleDraftHandler>(provider =>
    new ApplyMonitoringRuleDraftHandler(
        provider.GetRequiredService<IMonitoringRuleStore>(),
        provider.GetRequiredService<IClock>()));
builder.Services.AddScoped<ScheduleMonitoringRuleDraftHandler>(provider =>
    new ScheduleMonitoringRuleDraftHandler(
        provider.GetRequiredService<IMonitoringRuleStore>(),
        provider.GetRequiredService<IClock>()));
builder.Services.AddScoped<EditMonitoringRuleRevisionHandler>(provider =>
    new EditMonitoringRuleRevisionHandler(
        provider.GetRequiredService<IMonitoringRuleStore>(),
        provider.GetRequiredService<IClock>()));
builder.Services.AddScoped<RemoveMonitoringRuleRevisionHandler>(provider =>
    new RemoveMonitoringRuleRevisionHandler(
        provider.GetRequiredService<IMonitoringRuleStore>(),
        provider.GetRequiredService<IClock>()));

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:TradingEngine is not configured. For local development set the " +
        "passwordless Azure Dev connection string via 'dotnet user-secrets' or the " +
        "ConnectionStrings__TradingEngine environment variable on the Rider run configuration " +
        "(see docs/rider-local-azure-dev.md). Deployed instances receive it from the App " +
        "Service ConnectionStrings__TradingEngine application setting.");
}

builder.Services.AddTradingEngineInfrastructure(connectionString);

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

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseMiddleware<EasyAuthPrincipalMiddleware>());

app.MapWatchedInstrumentEndpoints();
app.MapMonitoringRuleEndpoints();

app.Run();
