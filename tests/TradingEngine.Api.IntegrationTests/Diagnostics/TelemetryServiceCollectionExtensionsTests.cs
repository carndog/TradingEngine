using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using TradingEngine.Api.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class TelemetryServiceCollectionExtensionsTests
{
    [Test]
    public void AddApiTelemetry_WhenConnectionStringMissing_DoesNotRegisterTracerProvider()
    {
        ServiceCollection services = CreateServices();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.AddApiTelemetry(configuration, "Test");
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<TracerProvider>(), Is.Null);
    }

    [Test]
    public void AddApiTelemetry_WhenConnectionStringPresent_RegistersTracerProvider()
    {
        ServiceCollection services = CreateServices();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [TelemetryServiceCollectionExtensions.ConnectionStringConfigurationKey] =
                        "InstrumentationKey=00000000-0000-0000-0000-000000000000"
                })
            .Build();

        services.AddApiTelemetry(configuration, "Test");
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<TracerProvider>(), Is.Not.Null);
    }

    [Test]
    public void AddApiTelemetry_WhenConnectionStringMissing_RegistersStartupTelemetry()
    {
        ServiceCollection services = CreateServices();
        IConfiguration configuration = new ConfigurationBuilder().Build();

        services.AddApiTelemetry(configuration, "Test");
        using ServiceProvider provider = services.BuildServiceProvider();
        IEnumerable<IHostedService> hostedServices = provider.GetServices<IHostedService>();

        Assert.That(
            hostedServices.Any(service => service is StartupTelemetryHostedService),
            Is.True);
    }

    [Test]
    public void AddApiTelemetry_WhenConnectionStringPresent_RegistersStartupTelemetryAfterExporters()
    {
        ServiceCollection services = CreateServices();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [TelemetryServiceCollectionExtensions.ConnectionStringConfigurationKey] =
                        "InstrumentationKey=00000000-0000-0000-0000-000000000000"
                })
            .Build();

        services.AddApiTelemetry(configuration, "Test");
        using ServiceProvider provider = services.BuildServiceProvider();
        List<IHostedService> hostedServices = provider.GetServices<IHostedService>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(
                hostedServices[hostedServices.Count - 1],
                Is.TypeOf<StartupTelemetryHostedService>());
            Assert.That(
                hostedServices.Any(service => service.GetType().Name == "TelemetryHostedService"),
                Is.True);
            Assert.That(
                hostedServices.Any(service => service.GetType().Name == "ExporterRegistrationHostedService"),
                Is.True);
        });
    }

    private static ServiceCollection CreateServices()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<ApplicationVersionProvider>();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment());

        return services;
    }
}
