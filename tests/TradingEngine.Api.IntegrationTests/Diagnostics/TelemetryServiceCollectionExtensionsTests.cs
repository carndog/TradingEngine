using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TradingEngine.Api.Diagnostics;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

[TestFixture]
public sealed class TelemetryServiceCollectionExtensionsTests
{
    private const string SyntheticConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000";

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

    [TestCase("Development", "http://localhost:4318", null, nameof(TelemetryExportTarget.LocalOtlp))]
    [TestCase("development", "http://localhost:4318", null, nameof(TelemetryExportTarget.LocalOtlp))]
    [TestCase("Development", "http://localhost:4318", SyntheticConnectionString, nameof(TelemetryExportTarget.LocalOtlp))]
    [TestCase("Development", null, SyntheticConnectionString, nameof(TelemetryExportTarget.AzureMonitor))]
    [TestCase("Development", " ", SyntheticConnectionString, nameof(TelemetryExportTarget.AzureMonitor))]
    [TestCase("Development", null, null, nameof(TelemetryExportTarget.None))]
    [TestCase("Production", "http://localhost:4318", SyntheticConnectionString, nameof(TelemetryExportTarget.AzureMonitor))]
    [TestCase("Production", "http://localhost:4318", null, nameof(TelemetryExportTarget.None))]
    [TestCase("Production", null, SyntheticConnectionString, nameof(TelemetryExportTarget.AzureMonitor))]
    public void SelectExportTarget_ForEnvironmentAndSettings_ReturnsExpectedTarget(
        string environmentName,
        string? otlpEndpoint,
        string? connectionString,
        string expectedTarget)
    {
        IConfiguration configuration = CreateConfiguration(otlpEndpoint, connectionString);

        TelemetryExportTarget target =
            TelemetryServiceCollectionExtensions.SelectExportTarget(configuration, environmentName);

        Assert.That(target, Is.EqualTo(Enum.Parse<TelemetryExportTarget>(expectedTarget)));
    }

    [Test]
    public void AddApiTelemetry_WhenOtlpEndpointSetInDevelopmentWithoutConnectionString_RegistersAllSignalProviders()
    {
        ServiceCollection services = CreateServices();
        IConfiguration configuration = CreateConfiguration("http://localhost:4318", null);
        services.AddSingleton(configuration);

        services.AddApiTelemetry(configuration, "Development");
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetService<TracerProvider>(), Is.Not.Null);
            Assert.That(provider.GetService<MeterProvider>(), Is.Not.Null);
            Assert.That(provider.GetService<LoggerProvider>(), Is.Not.Null);
        });
    }

    [Test]
    public void AddApiTelemetry_WhenOtlpEndpointSetOutsideDevelopmentWithoutConnectionString_DoesNotRegisterTracerProvider()
    {
        ServiceCollection services = CreateServices();
        IConfiguration configuration = CreateConfiguration("http://localhost:4318", null);

        services.AddApiTelemetry(configuration, "Production");
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<TracerProvider>(), Is.Null);
    }

    private static IConfiguration CreateConfiguration(string? otlpEndpoint, string? connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [TelemetryServiceCollectionExtensions.OtlpEndpointConfigurationKey] = otlpEndpoint,
                    [TelemetryServiceCollectionExtensions.ConnectionStringConfigurationKey] = connectionString
                })
            .Build();
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
