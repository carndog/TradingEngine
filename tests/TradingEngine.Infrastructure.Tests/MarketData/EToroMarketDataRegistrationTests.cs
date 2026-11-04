using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Infrastructure.MarketData;

namespace TradingEngine.Infrastructure.Tests.MarketData;

[TestFixture]
public sealed class EToroMarketDataRegistrationTests
{
    [Test]
    public void AddEtoroMarketData_WithConfiguration_ResolvesMarketDataProvider()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["EToro:MarketData:ApiKey"] = "test-api-key",
            ["EToro:MarketData:UserKey"] = "test-user-key"
        });

        IMarketDataProvider marketData = provider.GetRequiredService<IMarketDataProvider>();

        Assert.That(marketData, Is.TypeOf<EToroMarketDataProvider>());
    }

    [Test]
    public void AddEtoroMarketData_Always_RegistersSharedCollaborators()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["EToro:MarketData:ApiKey"] = "test-api-key",
            ["EToro:MarketData:UserKey"] = "test-user-key"
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                provider.GetRequiredService<ProviderRateGate>(),
                Is.SameAs(provider.GetRequiredService<ProviderRateGate>()));
            Assert.That(
                provider.GetRequiredService<EToroInstrumentIdCache>(),
                Is.SameAs(provider.GetRequiredService<EToroInstrumentIdCache>()));
            Assert.That(provider.GetRequiredService<TimeProvider>(),
                Is.SameAs(TimeProvider.System));
            Assert.That(provider.GetRequiredService<IClock>(),
                Is.SameAs(SystemClock.Instance));
        });
    }

    private static ServiceProvider BuildProvider(IReadOnlyDictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        ServiceCollection services = new();
        services.AddEtoroMarketData(configuration);
        return services.BuildServiceProvider();
    }
}
