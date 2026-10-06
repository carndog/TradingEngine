using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Testing;
using TradingEngine.Application.Ports;
using TradingEngine.Contracts.MonitoringRules;
using TradingEngine.Contracts.WatchedInstruments;
using TradingEngine.Domain.MonitoringRules;

namespace TradingEngine.Api.IntegrationTests.MonitoringRules;

internal sealed class MonitoringRuleApiHost : IDisposable
{
    public static readonly Guid RuleId = Guid.Parse("d4000000-0000-0000-0000-000000000001");
    public static readonly Guid InstrumentId = Guid.Parse("d4000000-0000-0000-0000-000000000002");
    public static readonly Guid InitialRevisionId = Guid.Parse("d4000000-0000-0000-0000-000000000003");
    public static readonly Instant January2 = Instant.FromUtc(2026, 1, 2, 9, 30);
    public static readonly Instant Now = Instant.FromUtc(2026, 10, 5, 9, 0);
    public const string SeedAuthor = "seed-user";

    private readonly WebApplicationFactory<Program> _factory;

    public MonitoringRuleApiHost(IDictionary<string, string?>? settings = null)
    {
        Store = new StubMonitoringRuleStore();
        Clock = new FakeClock(Now);
        Dictionary<string, string?> configuration = new()
        {
            ["ConnectionStrings:TradingEngine"] =
                "Server=localhost;Database=TradingEngineApiTests;Trusted_Connection=True;Encrypt=False"
        };
        if (settings is not null)
        {
            foreach (KeyValuePair<string, string?> setting in settings)
            {
                configuration[setting.Key] = setting.Value;
            }
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(configuration));
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IMonitoringRuleStore>(Store);
                    services.AddSingleton<IClock>(Clock);
                });
            });
        Client = _factory.CreateClient();
    }

    public StubMonitoringRuleStore Store { get; }

    public FakeClock Clock { get; }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }

    public static string Path(string suffix = "")
    {
        return $"/api/watched-instruments/{InstrumentId}/monitoring-rule{suffix}";
    }

    public static string Path(Guid instrumentId, string suffix)
    {
        return $"/api/watched-instruments/{instrumentId}/monitoring-rule{suffix}";
    }

    public MonitoringRule SeedRule(Instant? createdAt = null)
    {
        MonitoringRule rule = MonitoringRule.Create(
            RuleId,
            InstrumentId,
            InitialRevisionId,
            Definition(100m),
            createdAt ?? January2,
            SeedAuthor).Value;
        Store.Seed(rule);

        return rule;
    }

    public async Task<string> GetTokenAsync()
    {
        MonitoringRuleTimelineResponse timeline = await GetTimelineAsync();

        return timeline.ConcurrencyToken;
    }

    public async Task<MonitoringRuleTimelineResponse> GetTimelineAsync()
    {
        HttpResponseMessage response = await Client.GetAsync(Path());
        MonitoringRuleTimelineResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleTimelineResponse>();
        Assert.That(body, Is.Not.Null);

        return body!;
    }

    public async Task<MonitoringRuleDraftsResponse> GetDraftsAsync()
    {
        HttpResponseMessage response = await Client.GetAsync(Path("/drafts"));
        MonitoringRuleDraftsResponse? body = await response.Content
            .ReadFromJsonAsync<MonitoringRuleDraftsResponse>();
        Assert.That(body, Is.Not.Null);

        return body!;
    }

    public async Task<string> SnapshotStateAsync()
    {
        string timeline = await Client.GetStringAsync(Path());
        string drafts = await Client.GetStringAsync(Path("/drafts"));

        return timeline + drafts;
    }

    public async Task<(Guid DraftId, string Token)> CreateDraftAsync(
        decimal level,
        string? changeReason = "synthetic draft",
        RevisionPeriodDto? proposedPeriod = null)
    {
        string token = await GetTokenAsync();
        HttpResponseMessage response = await SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest(changeReason, proposedPeriod, DefinitionDto(level)),
            token);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        MonitoringRuleDraftsResponse drafts = await GetDraftsAsync();
        MonitoringRuleRevisionDto draft = drafts.Drafts.Single(candidate =>
            candidate.Definition.SupportZones![0].Level == level);

        return (draft.Id, drafts.ConcurrencyToken);
    }

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? ifMatch)
    {
        HttpRequestMessage request = new(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return SendAsync(request, ifMatch);
    }

    public Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        string rawJson,
        string? ifMatch)
    {
        HttpRequestMessage request = new(method, path)
        {
            Content = new StringContent(rawJson, Encoding.UTF8, "application/json")
        };

        return SendAsync(request, ifMatch);
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string? ifMatch)
    {
        if (ifMatch is not null)
        {
            request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{ifMatch}\""));
        }

        return Client.SendAsync(request);
    }

    public static MonitoringRuleDefinitionDto DefinitionDto(decimal level)
    {
        return new MonitoringRuleDefinitionDto(
            4,
            [
                new ChartZoneDto(
                    "support-a",
                    level - 5m,
                    level,
                    level + 5m,
                    [
                        new ChartConditionDto("buy-zone", "publish-signal"),
                        new ChartConditionDto("support-loss", "publish-signal")
                    ])
            ],
            []);
    }

    public static ChartAnalysisDefinition Definition(decimal level)
    {
        return ChartAnalysisDefinition.Create(
            4,
            [
                ChartZone.Create(
                    ChartAnalysisIdentifier.From("support-a").Value,
                    level - 5m,
                    level,
                    level + 5m,
                    [
                        ChartCondition.Create(
                            ChartConditionType.BuyZone,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value,
                        ChartCondition.Create(
                            ChartConditionType.SupportLoss,
                            ChartAnalysisIdentifier.From("publish-signal").Value).Value
                    ]).Value
            ],
            []).Value;
    }

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        if (problem?.Extensions.TryGetValue("code", out object? code) is true
            && code is JsonElement element)
        {
            return element.GetString();
        }

        return null;
    }

    public static decimal Level(MonitoringRuleRevisionDto revision)
    {
        return revision.Definition.SupportZones![0].Level;
    }
}
