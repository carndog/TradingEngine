using Microsoft.Extensions.DependencyInjection;
using TradingEngine.Application.Ports;

namespace TradingEngine.Infrastructure.Notifications;

public static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddEmailSender(
        this IServiceCollection services,
        EmailNotificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<IEmailSender>(provider =>
            new AcsEmailSender(provider.GetRequiredService<EmailNotificationOptions>()));

        return services;
    }
}
