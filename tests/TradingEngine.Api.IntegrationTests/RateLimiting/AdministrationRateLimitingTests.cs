using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TradingEngine.Api.IntegrationTests.MonitoringRules;
using TradingEngine.Contracts.MonitoringRules;
using static TradingEngine.Api.IntegrationTests.MonitoringRules.MonitoringRuleApiHost;

namespace TradingEngine.Api.IntegrationTests.RateLimiting;

[TestFixture]
public sealed class AdministrationRateLimitingTests
{
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";
    private const string ObjectIdentifierClaim =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string OwnerA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string OwnerB = "bbbbbbbb-0000-0000-0000-000000000002";

    [Test]
    public async Task GetTimeline_WithinReadBudget_ReturnsOk()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 3));
        host.SeedRule();

        HttpResponseMessage first = await host.Client.GetAsync(Path());
        HttpResponseMessage second = await host.Client.GetAsync(Path());
        HttpResponseMessage third = await host.Client.GetAsync(Path());

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(third.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(host.Store.GetCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task GetTimeline_ExceedingReadBudget_Returns429WithoutTouchingStore()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 2));
        host.SeedRule();
        await host.Client.GetAsync(Path());
        await host.Client.GetAsync(Path());

        HttpResponseMessage rejected = await host.Client.GetAsync(Path());
        string? code = await ProblemCodeAsync(rejected);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.StatusCode, Is.EqualTo((HttpStatusCode)429));
            Assert.That(rejected.Headers.RetryAfter, Is.Not.Null);
            Assert.That(code, Is.EqualTo("rate_limit.exceeded"));
            Assert.That(host.Store.GetCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GetTimeline_AfterWindowElapses_ReadPermitsReplenish()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 1, windowSeconds: 1));
        host.SeedRule();
        HttpResponseMessage allowed = await host.Client.GetAsync(Path());
        HttpResponseMessage rejected = await host.Client.GetAsync(Path());

        HttpStatusCode? recovered = null;
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            HttpResponseMessage attempt = await host.Client.GetAsync(Path());
            if (attempt.StatusCode != (HttpStatusCode)429)
            {
                recovered = attempt.StatusCode;
                break;
            }

            await Task.Delay(50);
        }

        Assert.Multiple(() =>
        {
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(rejected.StatusCode, Is.EqualTo((HttpStatusCode)429));
            Assert.That(recovered, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task PostDraft_ExceedingWriteBudget_Returns429WhileReadsRemainAllowed()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 50, write: 1));
        host.SeedRule();
        string token = await host.GetTokenAsync();

        HttpResponseMessage first = await host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest("first", null, DefinitionDto(110m)),
            token);
        HttpResponseMessage rejected = await host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest("second", null, DefinitionDto(120m)),
            host.Store.CurrentToken);
        HttpResponseMessage read = await host.Client.GetAsync(Path());

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(rejected.StatusCode, Is.EqualTo((HttpStatusCode)429));
            Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(host.Store.SaveCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetRequests_TwoDeployedCallers_HaveIndependentReadBudgets()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled(Limits(read: 1)));
        host.SeedRule();

        HttpResponseMessage ownerAFirst = await host.Client.SendAsync(GetWithPrincipal(OwnerA));
        HttpResponseMessage ownerARejected = await host.Client.SendAsync(GetWithPrincipal(OwnerA));
        HttpResponseMessage ownerB = await host.Client.SendAsync(GetWithPrincipal(OwnerB));

        Assert.Multiple(() =>
        {
            Assert.That(ownerAFirst.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(ownerARejected.StatusCode, Is.EqualTo((HttpStatusCode)429));
            Assert.That(ownerB.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task GetRequests_SpoofedPrincipalHeadersWithoutTrust_ShareSingleBoundedBudget()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 1));
        host.SeedRule();

        HttpResponseMessage first = await host.Client.SendAsync(GetWithPrincipal(OwnerA));
        HttpResponseMessage second = await host.Client.SendAsync(GetWithPrincipal(OwnerB));

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo((HttpStatusCode)429));
        });
    }

    [Test]
    public async Task GetTimeline_ConcurrencyPermitExhausted_RejectsWithoutRetryAfterAndReleasesPermit()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 50, concurrency: 1));
        host.SeedRule();
        TaskCompletionSource<object?> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Store.ReadGate = gate.Task;
        Task<HttpResponseMessage> blocked = host.Client.GetAsync(Path());
        await WaitForAsync(() => host.Store.ActiveReads == 1);

        HttpResponseMessage rejected = await host.Client.GetAsync(Path());
        gate.SetResult(null);
        HttpResponseMessage released = await blocked;
        HttpResponseMessage next = await host.Client.GetAsync(Path());

        Assert.Multiple(() =>
        {
            Assert.That(rejected.StatusCode, Is.EqualTo((HttpStatusCode)429));
            Assert.That(rejected.Headers.Contains("Retry-After"), Is.False);
            Assert.That(released.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(next.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(host.Store.GetCount, Is.EqualTo(2));
            Assert.That(host.Store.MaxActiveReads, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetTimeline_WhenHandlerThrows_ConcurrencyPermitIsReleased()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 50, concurrency: 1));
        host.SeedRule();
        host.Store.ThrowOnRead = true;

        HttpResponseMessage failed = await host.Client.GetAsync(Path());
        host.Store.ThrowOnRead = false;
        HttpResponseMessage next = await host.Client.GetAsync(Path());

        Assert.Multiple(() =>
        {
            Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(next.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task GetTimeline_WhenClientCancels_ConcurrencyPermitIsReleased()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 50, concurrency: 1));
        host.SeedRule();
        TaskCompletionSource<object?> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Store.ReadGate = gate.Task;
        using CancellationTokenSource cancelled = new();
        Task<HttpResponseMessage> blocked = host.Client.GetAsync(Path(), cancelled.Token);
        await WaitForAsync(() => host.Store.ActiveReads == 1);

        cancelled.Cancel();
        await Assert.CatchAsync<OperationCanceledException>(async () => await blocked);
        await WaitForAsync(() => host.Store.ActiveReads == 0);
        host.Store.ReadGate = null;
        gate.SetResult(null);
        HttpResponseMessage next = await host.Client.GetAsync(Path());

        Assert.That(next.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Diagnostics_WhenCallerBudgetExhausted_RemainAvailable()
    {
        using MonitoringRuleApiHost host = new(Limits(read: 1));
        host.SeedRule();
        await host.Client.GetAsync(Path());
        HttpResponseMessage rejected = await host.Client.GetAsync(Path());

        HttpResponseMessage health = await host.Client.GetAsync("/health");
        HttpResponseMessage version = await host.Client.GetAsync("/version");
        HttpResponseMessage authCheck = await host.Client.GetAsync("/auth-check");

        Assert.Multiple(() =>
        {
            Assert.That(rejected.StatusCode, Is.EqualTo((HttpStatusCode)429));
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(version.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(authCheck.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task HealthDatabase_ExceedingSharedProbeBudget_Returns429AndKeepsKeyCheckFirst()
    {
        Dictionary<string, string?> settings = Limits(read: 50, probe: 2);
        settings["Diagnostics:DatabaseProbeKey"] = "synthetic-probe-key";
        using MonitoringRuleApiHost host = new(settings);
        host.Client.DefaultRequestHeaders.Add("X-Database-Probe-Key", "synthetic-probe-key");

        HttpResponseMessage first = await host.Client.GetAsync("/health/database");
        HttpResponseMessage second = await host.Client.GetAsync("/health/database");
        HttpResponseMessage rejected = await host.Client.GetAsync("/health/database");
        host.Client.DefaultRequestHeaders.Remove("X-Database-Probe-Key");
        HttpResponseMessage unkeyed = await host.Client.GetAsync("/health/database");
        HttpResponseMessage health = await host.Client.GetAsync("/health");

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(rejected.StatusCode, Is.EqualTo((HttpStatusCode)429));
            Assert.That(unkeyed.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public void Host_WithWriteLimitAboveReadLimit_FailsStartupValidation()
    {
        Assert.Throws<OptionsValidationException>(
            () => _ = new MonitoringRuleApiHost(Limits(read: 1, write: 2)));
    }

    [Test]
    public void Host_WithZeroConcurrencyLimit_FailsStartupValidation()
    {
        Assert.Throws<OptionsValidationException>(
            () => _ = new MonitoringRuleApiHost(Limits(read: 5, concurrency: 0)));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (condition() is false && elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(25);
        }

        Assert.That(condition(), Is.True);
    }

    private static HttpRequestMessage GetWithPrincipal(string objectId)
    {
        HttpRequestMessage request = new(HttpMethod.Get, Path());
        request.Headers.Add(PrincipalHeader, EncodePrincipal(objectId));

        return request;
    }

    private static string EncodePrincipal(string objectId)
    {
        string json = $$"""
            {"auth_typ":"aad","claims":[{"typ":"{{ObjectIdentifierClaim}}","val":"{{objectId}}"}]}
            """;

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static Dictionary<string, string?> TrustEnabled(Dictionary<string, string?> settings)
    {
        settings["Authentication:EasyAuth:TrustPlatformHeaders"] = "true";

        return settings;
    }

    private static Dictionary<string, string?> Limits(
        int read,
        int? write = null,
        int concurrency = 4,
        int probe = 10,
        int windowSeconds = 3600)
    {
        return new Dictionary<string, string?>
        {
            ["RateLimiting:WindowSeconds"] = windowSeconds.ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:ReadPermitLimit"] = read.ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:WritePermitLimit"] = (write ?? read).ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:ConcurrencyPermitLimit"] = concurrency.ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:DatabaseProbePermitLimit"] = probe.ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:QueueLimit"] = "0"
        };
    }
}
