using NodaTime;
using TradingEngine.Application.MonitoringRules.Drafts;
using TradingEngine.Application.MonitoringRules.Lifecycle;
using TradingEngine.Application.MonitoringRules.Read;
using TradingEngine.Application.Ports;

namespace TradingEngine.Api.MonitoringRules;

internal static class MonitoringRuleServiceCollectionExtensions
{
    public static IServiceCollection AddMonitoringRules(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<GetMonitoringRuleTimelineHandler>(provider =>
            new GetMonitoringRuleTimelineHandler(
                provider.GetRequiredService<IMonitoringRuleStore>()));
        services.AddScoped<GetApplicableMonitoringRuleRevisionHandler>(provider =>
            new GetApplicableMonitoringRuleRevisionHandler(
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<CreateMonitoringRuleDraftHandler>(provider =>
            new CreateMonitoringRuleDraftHandler(
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<EditMonitoringRuleDraftHandler>(provider =>
            new EditMonitoringRuleDraftHandler(
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<DeleteMonitoringRuleDraftHandler>(provider =>
            new DeleteMonitoringRuleDraftHandler(
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<ApplyMonitoringRuleDraftHandler>(provider =>
            new ApplyMonitoringRuleDraftHandler(
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<ScheduleMonitoringRuleDraftHandler>(provider =>
            new ScheduleMonitoringRuleDraftHandler(
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<EditMonitoringRuleRevisionHandler>(provider =>
            new EditMonitoringRuleRevisionHandler(
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<IClock>()));
        services.AddScoped<RemoveMonitoringRuleRevisionHandler>(provider =>
            new RemoveMonitoringRuleRevisionHandler(
                provider.GetRequiredService<IMonitoringRuleStore>(),
                provider.GetRequiredService<IClock>()));

        return services;
    }
}
