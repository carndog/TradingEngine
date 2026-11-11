using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;
using TradingEngine.Api.Recommendations;
using TradingEngine.Application.Notifications;
using TradingEngine.Application.Ports;
using TradingEngine.Infrastructure.Notifications;

namespace TradingEngine.Api.Notifications;

internal static class NotificationsServiceCollectionExtensions
{
    public static IServiceCollection AddNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        NotificationOptions options = configuration
            .GetSection(NotificationOptions.SectionName)
            .Get<NotificationOptions>() ?? new NotificationOptions();

        services.AddSingleton(options);
        services.TryAddSingleton<RecommendationTelemetry>();
        services.AddSingleton<RecommendationEmailRenderer>();

        if (options.Enabled is false)
        {
            return services;
        }

        if (options.PollIntervalSeconds < 1)
        {
            throw new InvalidOperationException(
                "Notifications:PollIntervalSeconds must be at least 1 when dispatch " +
                "is enabled.");
        }

        if (options.MaxIntentsPerRun < 1)
        {
            throw new InvalidOperationException(
                "Notifications:MaxIntentsPerRun must be at least 1 when dispatch " +
                "is enabled.");
        }

        if (options.ClaimLeaseSeconds < 1)
        {
            throw new InvalidOperationException(
                "Notifications:ClaimLeaseSeconds must be at least 1 when dispatch " +
                "is enabled.");
        }

        if (options.MaxDispatchAttempts < 1)
        {
            throw new InvalidOperationException(
                "Notifications:MaxDispatchAttempts must be at least 1 when dispatch " +
                "is enabled.");
        }

        EmailNotificationOptions email = configuration
            .GetSection(EmailNotificationOptions.SectionName)
            .Get<EmailNotificationOptions>() ?? new EmailNotificationOptions();

        ValidateEmail(email);
        services.AddEmailSender(email);

        services.AddScoped<DispatchPendingNotificationsHandler>(provider =>
            new DispatchPendingNotificationsHandler(
                provider.GetRequiredService<INotificationIntentStore>(),
                provider.GetRequiredService<IEmailSender>(),
                provider.GetRequiredService<RecommendationEmailRenderer>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<NotificationOptions>()));

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService(provider =>
            new NotificationDispatchHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<NotificationDispatchHostedService>>(),
                provider.GetRequiredService<RecommendationTelemetry>(),
                options.PollIntervalSeconds));

        return services;
    }

    private static void ValidateEmail(EmailNotificationOptions email)
    {
        if (string.IsNullOrWhiteSpace(email.ConnectionString))
        {
            throw new InvalidOperationException(
                "Notifications:Email:ConnectionString is required when dispatch " +
                "is enabled; supply it via user secrets or a deployment secret, " +
                "never in committed configuration.");
        }

        if (string.IsNullOrWhiteSpace(email.SenderAddress))
        {
            throw new InvalidOperationException(
                "Notifications:Email:SenderAddress is required when dispatch " +
                "is enabled.");
        }

        if (email.RecipientAddresses.Length == 0
            || email.RecipientAddresses.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "Notifications:Email:RecipientAddresses requires at least one " +
                "non-empty address when dispatch is enabled.");
        }
    }
}
