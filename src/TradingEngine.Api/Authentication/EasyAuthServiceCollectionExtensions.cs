namespace TradingEngine.Api.Authentication;

internal static class EasyAuthServiceCollectionExtensions
{
    public static IServiceCollection AddEasyAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<EasyAuthOptions>(
            configuration.GetSection(EasyAuthOptions.SectionName));

        return services;
    }
}
