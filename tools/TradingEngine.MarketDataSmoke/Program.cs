using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingEngine.Application.MarketData;
using TradingEngine.Application.Ports;
using TradingEngine.Infrastructure.MarketData;
using TradingEngine.MarketDataSmoke;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tools/TradingEngine.MarketDataSmoke -- " +
        "<exchange> <symbol> <quoteCurrency>");
    Console.Error.WriteLine(
        "Requires the EToro__MarketData__ApiKey and EToro__MarketData__UserKey " +
        "environment variables and an EToro__MarketData__ExchangeMap__<exchange>__0 " +
        "entry matching the provider's internal exchange identity.");
    return 2;
}

string exchange = args[0];
string symbol = args[1];
string quoteCurrency = args[2];

IConfiguration configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

if (string.IsNullOrWhiteSpace(
        configuration[EToroMarketDataOptions.SectionName + ":ApiKey"])
    || string.IsNullOrWhiteSpace(
        configuration[EToroMarketDataOptions.SectionName + ":UserKey"]))
{
    Console.Error.WriteLine(
        "EToro:MarketData credentials are not configured. Set the " +
        "EToro__MarketData__ApiKey and EToro__MarketData__UserKey environment " +
        "variables before running; values are never printed.");
    return 2;
}

ServiceCollection services = new();
services.AddLogging(builder => builder.AddSimpleConsole());
services.AddEtoroMarketData(configuration);

await using ServiceProvider provider = services.BuildServiceProvider();
IMarketDataProvider marketData = provider.GetRequiredService<IMarketDataProvider>();

return await SmokeRunner.RunAsync(
    marketData,
    new MarketInstrumentIdentity(exchange, symbol, quoteCurrency),
    Console.Out,
    CancellationToken.None);
