using System.Text.Json;
using System.Text.Json.Serialization;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;

namespace TradingEngine.Client.Tests.Services;

internal static class ClientTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static WatchedInstrumentSettingsResponse Settings(
        Guid id,
        string symbol = "XTEST",
        string state = "configured",
        int interval = 60,
        string token = "tok-1")
    {
        return new WatchedInstrumentSettingsResponse(
            id,
            symbol,
            "XTST",
            "USD",
            state,
            interval,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            token);
    }

    internal static MonitoringRuleRevisionDto Revision(
        Guid id,
        string kind = "draft",
        int? revisionNumber = null,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null,
        DateTimeOffset? proposedFrom = null,
        DateTimeOffset? proposedTo = null,
        string? changeReason = null,
        bool createdByCaller = false,
        DateTimeOffset? createdAt = null,
        string createdBy = "synthetic-user")
    {
        return new MonitoringRuleRevisionDto(
            id,
            kind,
            revisionNumber,
            createdAt ?? DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            createdBy,
            createdByCaller,
            changeReason,
            effectiveFrom,
            effectiveTo,
            proposedFrom,
            proposedTo,
            Definition());
    }

    internal static MonitoringRuleDefinitionDto Definition()
    {
        return new MonitoringRuleDefinitionDto(
            2,
            [new ChartZoneDto("support-a", 90m, 95m, 100m, [new ChartConditionDto("buy-zone", "act-1"), new ChartConditionDto("support-loss", "act-2")])],
            []);
    }

    internal static MonitoringRuleTimelineResponse Timeline(
        string token,
        params MonitoringRuleRevisionDto[] revisions)
    {
        return new MonitoringRuleTimelineResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            token,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            revisions);
    }

    internal static MonitoringRuleDraftsResponse Drafts(
        string token,
        params MonitoringRuleRevisionDto[] drafts)
    {
        return new MonitoringRuleDraftsResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            token,
            drafts);
    }

    internal static MonitoringRuleRevisionResponse RevisionResponse(
        string token,
        MonitoringRuleRevisionDto revision)
    {
        return new MonitoringRuleRevisionResponse(token, revision);
    }

    internal static string JsonOf<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
