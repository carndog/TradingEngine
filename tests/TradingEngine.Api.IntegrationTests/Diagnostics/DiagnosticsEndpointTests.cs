using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class DiagnosticsEndpointTests
{
    private WebApplicationFactory<Program> _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new WebApplicationFactory<Program>();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task Health_WhenRequested_ReturnsSuccessfulResponse()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");
        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Status, Is.EqualTo("Healthy"));
        });
    }

    [Test]
    public async Task HealthDatabase_WhenProbeKeyNotConfigured_ReturnsNotFound()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/database");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task HealthDatabase_WhenProbeKeyMissing_ReturnsNotFound()
    {
        using WebApplicationFactory<Program> factory = CreateFactoryWithProbeKey("synthetic-probe-key");
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health/database");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task HealthDatabase_WhenProbeKeyIncorrect_ReturnsNotFound()
    {
        using WebApplicationFactory<Program> factory = CreateFactoryWithProbeKey("synthetic-probe-key");
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Database-Probe-Key", "wrong-key");

        HttpResponseMessage response = await client.GetAsync("/health/database");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task HealthDatabase_WhenProbeKeyMatchesAndDatabaseNotConfigured_ReturnsUnhealthy()
    {
        using WebApplicationFactory<Program> factory = CreateFactoryWithProbeKey("synthetic-probe-key");
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Database-Probe-Key", "synthetic-probe-key");

        HttpResponseMessage response = await client.GetAsync("/health/database");
        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Status, Is.EqualTo("Unhealthy"));
        });
    }

    [Test]
    public async Task Health_WhenDatabaseNotConfigured_ReturnsHealthy()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");
        HealthResponse? body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Not.Null);
            Assert.That(body!.Status, Is.EqualTo("Healthy"));
        });
    }

    [Test]
    public async Task Version_WhenRequested_ReturnsBuildIdentityWithoutConfiguration()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/version");
        VersionResponse? body = await response.Content.ReadFromJsonAsync<VersionResponse>();
        Assert.That(body, Is.Not.Null);
        VersionResponse version = body!;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(version.Application, Is.EqualTo("TradingEngine.Api"));
            Assert.That(version.Version, Is.Not.Empty);
            Assert.That(version.Commit, Is.Not.Empty);
        });
    }

    private WebApplicationFactory<Program> CreateFactoryWithProbeKey(string probeKey)
    {
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Diagnostics:DatabaseProbeKey"] = probeKey
                    })));
    }
}
