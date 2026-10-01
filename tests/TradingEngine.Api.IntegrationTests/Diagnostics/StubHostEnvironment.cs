using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace TradingEngine.Api.IntegrationTests.Diagnostics;

internal sealed class StubHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";

    public string ApplicationName { get; set; } = "TradingEngine.Api";

    public string ContentRootPath { get; set; } = string.Empty;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
