using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
}
