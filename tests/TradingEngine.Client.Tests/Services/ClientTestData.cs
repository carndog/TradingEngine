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

    internal static ListingDto Listing(
        string unitState = "unconfirmed",
        string? nativeQuoteCurrency = "GBX",
        string? unitSource = null,
        bool deploymentConfigured = false,
        string? concurrencyToken = "listing-tok-1",
        string? resolutionState = "resolved",
        string? provider = "eToro")
    {
        return new ListingDto(
            resolutionState,
            null,
            provider,
            provider is null ? null : 4242,
            provider is null ? null : "XTEST",
            provider is null ? null : "Test Exchange",
            provider is null ? null : "Synthetic Instrument",
            unitState,
            nativeQuoteCurrency,
            unitSource,
            deploymentConfigured,
            concurrencyToken);
    }

    internal static CollectionStatusDto Collection(
        string status,
        DateTimeOffset? lastObservedAt = null,
        string? lastAttemptErrorCode = null)
    {
        return new CollectionStatusDto(
            status,
            null,
            null,
            lastObservedAt,
            null,
            lastAttemptErrorCode is null ? "Observed" : "Failed",
            lastAttemptErrorCode);
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
