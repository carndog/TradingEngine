using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Application.Ports;
using TradingEngine.Application.Signals;

namespace TradingEngine.Api.IntegrationTests.WeeklyReports;

[TestFixture]
public sealed class WeeklyReportEndpointTests : ISignalEvidenceReader
{
    private int _reads;

    [SetUp]
    public void SetUp()
    {
        _reads = 0;
    }

    [Test]
    public async Task Get_PreviewWithSendingDisabled_ReturnsBoundedContentWithoutStoreOrSender()
    {
        using WebApplicationFactory<Program> factory = Factory(false);
        HttpClient client = factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/weekly-reports/preview?cutoff=2026-03-29T17:00:00Z");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body.RootElement.GetProperty("cutoff").GetString(), Is.EqualTo("2026-03-29T17:00:00+00:00"));
        Assert.That(body.RootElement.GetProperty("periodStart").GetString(), Is.EqualTo("2026-03-22T18:00:00+00:00"));
        Assert.That(body.RootElement.GetProperty("items").GetArrayLength(), Is.Zero);
        Assert.That(body.RootElement.TryGetProperty("recipientAddresses", out _), Is.False);
        Assert.That(_reads, Is.EqualTo(2));
        string cutoff = body.RootElement.GetProperty("cutoff").GetString()!;
        Assert.That((await client.GetAsync($"/api/weekly-reports/preview?cutoff={Uri.EscapeDataString(cutoff)}&page=2")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("page=0")]
    [TestCase("pageSize=101")]
    [TestCase("cutoff=invalid")]
    [TestCase("cutoff=2999-01-01T00:00:00Z")]
    [TestCase("cutoff=2026-01-01T00:00:00.123456789Z")]
    public async Task Get_InvalidPreview_RejectsBeforeSourceRead(string query)
    {
        using WebApplicationFactory<Program> factory = Factory(false);
        HttpResponseMessage response = await factory.CreateClient().GetAsync($"/api/weekly-reports/preview?{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(_reads, Is.Zero);
    }

    [TestCase("/api/weekly-reports")]
    [TestCase("/api/weekly-reports/preview")]
    [TestCase("/api/weekly-reports/00000000-0000-0000-0000-000000000001")]
    public async Task Get_Unauthenticated_RejectsBeforeDatabaseOrSourceRead(string path)
    {
        using WebApplicationFactory<Program> factory = Factory(true);
        HttpResponseMessage response = await factory.CreateClient().GetAsync(path);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(_reads, Is.Zero);
    }

    [TestCase("/api/weekly-reports?maximum=101")]
    [TestCase("/api/weekly-reports/00000000-0000-0000-0000-000000000001?pageSize=0")]
    public async Task Get_InvalidSavedReportPagination_RejectsBeforeDatabaseRead(string path)
    {
        using WebApplicationFactory<Program> factory = Factory(false);
        Assert.That((await factory.CreateClient().GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void CreateClient_EnabledWithoutPrivateConfiguration_RejectsStartup()
    {
        using WebApplicationFactory<Program> factory = Factory(false, true);
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    public Task<IReadOnlyList<SignalEvidence>> ReadAsync(SignalQuery query, CancellationToken cancellationToken)
    {
        _reads++;
        return Task.FromResult<IReadOnlyList<SignalEvidence>>([]);
    }

    private WebApplicationFactory<Program> Factory(bool authenticated, bool enabled = false)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSyntheticConnectionString();
            builder.UseSetting("WeeklyReports:Enabled", enabled.ToString());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:EasyAuth:TrustPlatformHeaders"] = authenticated.ToString()
            }));
            builder.ConfigureServices(services => services.AddSingleton<ISignalEvidenceReader>(this));
        });
    }
}
