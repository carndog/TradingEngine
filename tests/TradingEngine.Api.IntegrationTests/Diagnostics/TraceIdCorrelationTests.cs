using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Api.IntegrationTests.WatchedInstruments;
using TradingEngine.Application.Ports;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class TraceIdCorrelationTests
{
    [Test]
    public async Task InternalError_WhenRequestFails_TraceIdMatchesRequestActivity()
    {
        StubWatchedInstrumentStore store = new()
        {
            SearchException = new InvalidOperationException("synthetic failure")
        };
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSyntheticConnectionString();
                builder.ConfigureServices(services =>
                {
                    services.AddHttpContextAccessor();
                    services.AddSingleton<IWatchedInstrumentStore>(store);
                });
            });

        Activity? capturedActivity = null;
        string? capturedTraceIdentifier = null;
        IHttpContextAccessor accessor =
            factory.Services.GetRequiredService<IHttpContextAccessor>();
        store.BeforeSearch = () =>
        {
            capturedActivity = Activity.Current;
            capturedTraceIdentifier = accessor.HttpContext?.TraceIdentifier;
        };

        HttpClient client = factory.CreateClient();
        HttpResponseMessage response =
            await client.GetAsync("/api/watched-instruments?page=1&pageSize=25");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));

        using JsonDocument problem =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string traceId = problem.RootElement.GetProperty("traceId").GetString()!;

        if (capturedActivity is not null)
        {
            Assert.Multiple(() =>
            {
                Assert.That(traceId, Is.EqualTo(capturedActivity.Id));
                Assert.That(
                    traceId,
                    Does.Contain(capturedActivity.TraceId.ToHexString()));
            });
        }
        else
        {
            Assert.That(traceId, Is.EqualTo(capturedTraceIdentifier));
        }
    }
}
