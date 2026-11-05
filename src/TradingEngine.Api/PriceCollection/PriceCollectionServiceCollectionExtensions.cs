using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;
using NodaTime.Text;
using TradingEngine.Application.Ports;
using TradingEngine.Application.PriceCollection;

namespace TradingEngine.Api.PriceCollection;

internal static class PriceCollectionServiceCollectionExtensions
{
    private static readonly LocalTimePattern SessionTimePattern =
        LocalTimePattern.CreateWithInvariantCulture("HH:mm");

    public static IServiceCollection AddPriceCollection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        PriceCollectionOptions options = configuration
            .GetSection(PriceCollectionOptions.SectionName)
            .Get<PriceCollectionOptions>() ?? new PriceCollectionOptions();

        services.AddSingleton(options);

        if (options.Enabled is false)
        {
            return services;
        }

        if (options.PollIntervalSeconds < 1)
        {
            throw new InvalidOperationException(
                "PriceCollection:PollIntervalSeconds must be at least 1 when the " +
                "collector is enabled.");
        }

        if (options.MaxExecutionTimeSeconds < 1)
        {
            throw new InvalidOperationException(
                "PriceCollection:MaxExecutionTimeSeconds must be at least 1 when the " +
                "collector is enabled.");
        }

        services.AddSingleton<IMarketSessionPolicy>(
            new WeeklyScheduleMarketSessionPolicy(BuildSessions(options)));
        services.AddSingleton<ISamplingPolicy>(SamplingPolicyResolver.Resolve(
            options.SamplingPolicy,
            options.ProviderRateLimit,
            options.ProviderWindowSeconds));
        services.AddScoped<CollectDuePricesHandler>(provider =>
            new CollectDuePricesHandler(
                provider.GetRequiredService<IMonitoredInstrumentSource>(),
                provider.GetRequiredService<IPriceObservationStore>(),
                provider.GetRequiredService<IMarketDataProvider>(),
                provider.GetRequiredService<ISamplingPolicy>(),
                provider.GetRequiredService<IMarketSessionPolicy>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<PriceCollectionOptions>()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService(provider =>
            new PriceCollectionHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<PriceCollectionHostedService>>(),
                options.PollIntervalSeconds));

        return services;
    }

    private static IReadOnlyDictionary<string, ExchangeSession> BuildSessions(
        PriceCollectionOptions options)
    {
        Dictionary<string, ExchangeSession> sessions =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, ExchangeSessionOptions> pair in options.ExchangeSessions)
        {
            sessions[pair.Key.Trim().ToUpperInvariant()] = BuildSession(pair.Key, pair.Value);
        }

        return sessions;
    }

    private static ExchangeSession BuildSession(
        string exchange,
        ExchangeSessionOptions options)
    {
        if (options.AlwaysOpen)
        {
            return new ExchangeSession(null, true, []);
        }

        if (string.IsNullOrWhiteSpace(options.TimeZone))
        {
            throw new InvalidOperationException(
                $"PriceCollection:ExchangeSessions:{exchange}:TimeZone is required unless " +
                "AlwaysOpen is set.");
        }

        DateTimeZone? zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(options.TimeZone);
        if (zone is null)
        {
            throw new InvalidOperationException(
                $"PriceCollection:ExchangeSessions:{exchange}:TimeZone " +
                $"'{options.TimeZone}' is not a recognised IANA time zone.");
        }

        if (options.Windows.Count == 0)
        {
            throw new InvalidOperationException(
                $"PriceCollection:ExchangeSessions:{exchange} requires at least one session " +
                "window unless AlwaysOpen is set.");
        }

        List<SessionWindow> windows = options.Windows
            .Select(window => BuildWindow(exchange, window))
            .ToList();

        return new ExchangeSession(zone, false, windows);
    }

    private static SessionWindow BuildWindow(
        string exchange,
        SessionWindowOptions options)
    {
        if (options.Days.Count == 0)
        {
            throw new InvalidOperationException(
                $"A session window for exchange '{exchange}' must name at least one day.");
        }

        HashSet<IsoDayOfWeek> days = [];
        foreach (string day in options.Days)
        {
            if (Enum.TryParse(day, true, out IsoDayOfWeek parsed) is false
                || Enum.IsDefined(parsed) is false)
            {
                throw new InvalidOperationException(
                    $"Session window day '{day}' for exchange '{exchange}' is not a " +
                    "recognised day of week.");
            }

            days.Add(parsed);
        }

        LocalTime open = ParseSessionTime(exchange, nameof(options.Open), options.Open);
        LocalTime close = ParseSessionTime(exchange, nameof(options.Close), options.Close);

        if (open == close)
        {
            throw new InvalidOperationException(
                $"A session window for exchange '{exchange}' has equal open and close " +
                "times; use AlwaysOpen for a continuous session.");
        }

        return new SessionWindow(days, open, close);
    }

    private static LocalTime ParseSessionTime(
        string exchange,
        string field,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Session window {field} for exchange '{exchange}' is required in HH:mm " +
                "format.");
        }

        ParseResult<LocalTime> parsed = SessionTimePattern.Parse(value);
        if (parsed.Success is false)
        {
            throw new InvalidOperationException(
                $"Session window {field} value '{value}' for exchange '{exchange}' is " +
                "not a valid HH:mm time.");
        }

        return parsed.Value;
    }
}
