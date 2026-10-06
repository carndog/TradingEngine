namespace TradingEngine.Api.Authentication;

public sealed class EasyAuthOptions
{
    public const string SectionName = "Authentication:EasyAuth";

    public bool TrustPlatformHeaders { get; set; }
}
