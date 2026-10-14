using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TradingEngine.Client;
using TradingEngine.Client.Services;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
builder.Services.AddScoped<TradingEngineApiClient>();
builder.Services.AddScoped<InstrumentListState>();
builder.Services.AddSingleton<IUiClock>(SystemUiClock.Instance);

await builder.Build().RunAsync();
