using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Application.Ports;
using TradingEngine.Infrastructure.MonitoringRules.Xml;
using TradingEngine.Infrastructure.Persistence;

namespace TradingEngine.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddTradingEngineInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<TradingEngineDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddSingleton<ChartAnalysisDefinitionXmlSerializer>();
        services.AddScoped<IWatchedInstrumentStore, SqlServerWatchedInstrumentStore>();

        return services;
    }
}
