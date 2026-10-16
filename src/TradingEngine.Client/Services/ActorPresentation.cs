using TradingEngine.Contracts.MonitoringRules;

namespace TradingEngine.Client.Services;

public static class ActorPresentation
{
    private const string UnverifiedLocalCaller = "unverified-local-caller";

    public static string Describe(MonitoringRuleRevisionDto revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (revision.CreatedByCaller)
        {
            return "You";
        }

        if (string.IsNullOrWhiteSpace(revision.CreatedBy))
        {
            return "Unrecorded user";
        }

        if (revision.CreatedBy == UnverifiedLocalCaller)
        {
            return "Unverified local caller";
        }

        return "Another user";
    }
}
