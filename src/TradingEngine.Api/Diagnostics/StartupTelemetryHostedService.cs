using TradingEngine.Contracts.Diagnostics;

namespace TradingEngine.Api.Diagnostics;

internal sealed class StartupTelemetryHostedService : IHostedService
{
    internal const string StartupEventName = "TradingEngine.Api.Started";

    private readonly ILogger<StartupTelemetryHostedService> _logger;
    private readonly ApplicationVersionProvider _versionProvider;
    private readonly IHostEnvironment _environment;

    public StartupTelemetryHostedService(
        ILogger<StartupTelemetryHostedService> logger,
        ApplicationVersionProvider versionProvider,
        IHostEnvironment environment)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        VersionResponse identity = _versionProvider.GetCurrent();

        _logger.LogInformation(
            "{microsoft.custom_event.name} {Application} {Version} {Commit} {Environment}",
            StartupEventName,
            identity.Application,
            identity.Version,
            identity.Commit,
            _environment.EnvironmentName);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
