using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TradingEngine.Api.Diagnostics;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddSingleton<ApplicationVersionProvider>();

WebApplication app = builder.Build();

app.MapHealthChecks(
        "/health",
        new HealthCheckOptions
        {
            ResponseWriter = HealthResponseWriter.WriteAsync
        })
    .AllowAnonymous();

app.MapGet(
        "/version",
        (ApplicationVersionProvider versionProvider) => TypedResults.Ok(versionProvider.GetCurrent()))
    .AllowAnonymous();

app.Run();
