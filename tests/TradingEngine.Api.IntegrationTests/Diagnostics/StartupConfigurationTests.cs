using Microsoft.AspNetCore.Mvc.Testing;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class StartupConfigurationTests
{
    [Test]
    public void Startup_WhenConnectionStringMissing_ThrowsActionableError()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting(ApiTestHost.ConnectionStringKey, null));

        Exception? thrown = Assert.Catch(() => factory.CreateClient());

        AssertActionableError(thrown);
    }

    [Test]
    public void Startup_WhenConnectionStringBlank_ThrowsActionableError()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting(ApiTestHost.ConnectionStringKey, "   "));

        Exception? thrown = Assert.Catch(() => factory.CreateClient());

        AssertActionableError(thrown);
    }

    [Test]
    public void Startup_WhenConnectionStringConfigured_BuildsClient()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSyntheticConnectionString());

        using HttpClient client = factory.CreateClient();

        Assert.That(client, Is.Not.Null);
    }

    private static void AssertActionableError(Exception? thrown)
    {
        List<Exception> chain = [];
        for (Exception? current = thrown; current is not null; current = current.InnerException)
        {
            chain.Add(current);
        }

        InvalidOperationException? actionable = chain
            .OfType<InvalidOperationException>()
            .FirstOrDefault(candidate =>
                candidate.Message.Contains("ConnectionStrings:TradingEngine", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.Not.Null);
            Assert.That(actionable, Is.Not.Null);
            Assert.That(actionable?.Message, Does.Contain("user-secrets"));
        });
    }
}
