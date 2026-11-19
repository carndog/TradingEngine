using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;
using TradingEngine.Application.Ports;
using TradingEngine.Application.WeeklyReports;
using TradingEngine.Infrastructure.Notifications;

namespace TradingEngine.Api.WeeklyReports;

internal static class WeeklyReportsServiceCollectionExtensions
{
    public static IServiceCollection AddWeeklyReports(this IServiceCollection services, IConfiguration configuration)
    {
        WeeklyReportOptions options = configuration.GetSection(WeeklyReportOptions.SectionName).Get<WeeklyReportOptions>() ?? new();
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<WeeklyReportSchedule>();
        services.AddSingleton<WeeklyReportEmailRenderer>();
        services.AddScoped<WeeklyReportComposer>();
        if (options.Enabled)
        {
            EmailNotificationOptions email = configuration.GetSection(EmailNotificationOptions.SectionName).Get<EmailNotificationOptions>() ?? new();
            if (string.IsNullOrWhiteSpace(email.ConnectionString) || string.IsNullOrWhiteSpace(email.SenderAddress))
            {
                throw new InvalidOperationException("WeeklyReports requires private Notifications:Email connection and sender settings when enabled.");
            }

            if (services.Any(descriptor => descriptor.ServiceType == typeof(IEmailSender)) is false)
            {
                services.AddEmailSender(email);
            }
        }

        services.AddScoped<RunWeeklyReportsHandler>(provider => new RunWeeklyReportsHandler(
            provider.GetRequiredService<IWeeklyReportStore>(), provider.GetRequiredService<WeeklyReportSchedule>(),
            provider.GetRequiredService<WeeklyReportComposer>(), options, provider.GetRequiredService<IClock>(), provider.GetService<IEmailSender>()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<WeeklyReportTelemetry>();
        if (options.DeploymentId.Length > 0)
        {
            services.AddHostedService<WeeklyReportHostedService>();
        }

        return services;
    }
}
