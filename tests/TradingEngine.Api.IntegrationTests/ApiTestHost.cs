using Microsoft.AspNetCore.Hosting;

namespace TradingEngine.Api.IntegrationTests;

internal static class ApiTestHost
{
    internal const string ConnectionStringKey = "ConnectionStrings:TradingEngine";

    internal static IWebHostBuilder UseSyntheticConnectionString(this IWebHostBuilder builder)
    {
        return builder.UseSetting(
            ConnectionStringKey,
            "Server=localhost;Database=TradingEngineApiTests;Trusted_Connection=True;Encrypt=False");
    }
}
