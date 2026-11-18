using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Signals;

namespace TradingEngine.Api.IntegrationTests.Signals;

[TestFixture]
public sealed class SignalEndpointTests : ISignalEvidenceReader
{
    private WebApplicationFactory<Program> _factory = null!;
    private int _reads;

    [SetUp]
    public void SetUp()
    {
        _reads = 0;
        _factory = Factory(false);
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task Get_EmptyPage_ReturnsReusableUtcCutoff()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/signals");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body.RootElement.GetProperty("items").GetArrayLength(), Is.Zero);
        Assert.That(body.RootElement.GetProperty("cutoff").GetString(), Does.EndWith("+00:00"));
        string cutoff = body.RootElement.GetProperty("cutoff").GetString()!;
        HttpResponseMessage next = await client.GetAsync($"/api/signals?cutoff={Uri.EscapeDataString(cutoff)}&page=2");
        Assert.That(next.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("page=0")]
    [TestCase("pageSize=101")]
    [TestCase("cutoff=invalid")]
    [TestCase("triggeredFrom=")]
    [TestCase("cutoff=2999-01-01T00:00:00Z")]
    [TestCase("cutoff=2026-01-01T00:00:00.123456789Z")]
    public async Task Get_InvalidInput_RejectsBeforeReading(string query)
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync($"/api/signals?{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(_reads, Is.Zero);
    }

    [Test]
    public async Task Get_MissingSignal_ReturnsNotFound()
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync($"/api/signals/{Guid.NewGuid()}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Get_Unauthenticated_ReturnsUnauthorizedBeforeReading()
    {
        using WebApplicationFactory<Program> factory = Factory(true);
        HttpResponseMessage response = await factory.CreateClient().GetAsync("/api/signals");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_reads, Is.Zero);
    }

    public Task<IReadOnlyList<SignalEvidence>> ReadAsync(SignalQuery query, CancellationToken cancellationToken)
    {
        _reads++;
        return Task.FromResult<IReadOnlyList<SignalEvidence>>([]);
    }

    private WebApplicationFactory<Program> Factory(bool authenticated)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSyntheticConnectionString();
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:EasyAuth:TrustPlatformHeaders"] = authenticated.ToString() }));
            builder.ConfigureServices(services => services.AddSingleton<ISignalEvidenceReader>(this));
        });
    }
}
