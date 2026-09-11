using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.Tests.Diagnostics;

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
    public async Task Health_returns_a_successful_public_safe_response()
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
    public async Task Version_identifies_the_application_build_without_configuration_values()
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
