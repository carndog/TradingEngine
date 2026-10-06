using System.Net;
using System.Net.Http.Json;
using System.Text;
using TradingEngine.Api.IntegrationTests.MonitoringRules;
using TradingEngine.Contracts.MonitoringRules;
using static TradingEngine.Api.IntegrationTests.MonitoringRules.MonitoringRuleApiHost;

namespace TradingEngine.Api.IntegrationTests.Authentication;

[TestFixture]
public sealed class EasyAuthPrincipalMiddlewareTests
{
    private const string ObjectId = "11111111-2222-3333-4444-555555555555";
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";

    private static readonly Dictionary<string, string?> TrustEnabled = new()
    {
        ["Authentication:EasyAuth:TrustPlatformHeaders"] = "true"
    };

    [Test]
    public async Task PostDraft_WithTrustedPrincipal_RecordsStableObjectIdentifierAsCreatedBy()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.SeedRule();
        host.Client.DefaultRequestHeaders.Add(PrincipalHeader, EncodePrincipal(
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", ObjectId),
            ("preferred_username", "synthetic.owner@example.test"),
            ("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", "subject-value")));
        string token = await host.GetTokenAsync();

        HttpResponseMessage response = await host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest("owner draft", null, DefinitionDto(110m)),
            token);
        MonitoringRuleDraftsResponse drafts = await host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(drafts.Drafts[0].CreatedBy, Is.EqualTo(ObjectId));
        });
    }

    [Test]
    public async Task PostDraft_WithTrustedPrincipalLackingObjectId_FallsBackToSubject()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.SeedRule();
        host.Client.DefaultRequestHeaders.Add(PrincipalHeader, EncodePrincipal(
            ("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", "subject-value")));
        string token = await host.GetTokenAsync();

        await host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest("subject draft", null, DefinitionDto(110m)),
            token);
        MonitoringRuleDraftsResponse drafts = await host.GetDraftsAsync();

        Assert.That(drafts.Drafts[0].CreatedBy, Is.EqualTo("subject-value"));
    }

    [Test]
    public async Task GetTimeline_WithTrustEnabledAndNoPrincipalHeader_ReturnsUnauthorizedProblem()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.SeedRule();

        HttpResponseMessage response = await host.Client.GetAsync(Path());
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(code, Is.EqualTo("authentication.principal_required"));
        });
    }

    [Test]
    public async Task GetTimeline_WithTrustEnabledAndMalformedPrincipal_ReturnsUnauthorizedProblem()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.SeedRule();
        host.Client.DefaultRequestHeaders.Add(PrincipalHeader, "not-base64!!");

        HttpResponseMessage response = await host.Client.GetAsync(Path());
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(code, Is.EqualTo("authentication.principal_malformed"));
        });
    }

    [Test]
    public async Task GetTimeline_WithTrustEnabledAndPrincipalWithoutIdentifier_ReturnsUnauthorizedProblem()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.SeedRule();
        host.Client.DefaultRequestHeaders.Add(PrincipalHeader, EncodePrincipal(
            ("preferred_username", "synthetic.owner@example.test")));

        HttpResponseMessage response = await host.Client.GetAsync(Path());
        string? code = await ProblemCodeAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(code, Is.EqualTo("authentication.principal_identifier_missing"));
        });
    }

    [Test]
    public async Task GetTimeline_WithTrustEnabledAndNonJsonPayload_ReturnsUnauthorizedProblem()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);
        host.SeedRule();
        host.Client.DefaultRequestHeaders.Add(
            PrincipalHeader,
            Convert.ToBase64String(Encoding.UTF8.GetBytes("plain text")));

        HttpResponseMessage response = await host.Client.GetAsync(Path());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AnonymousEndpoints_WithTrustEnabledAndNoPrincipal_RemainReachable()
    {
        using MonitoringRuleApiHost host = new(TrustEnabled);

        HttpResponseMessage health = await host.Client.GetAsync("/health");
        HttpResponseMessage version = await host.Client.GetAsync("/version");

        Assert.Multiple(() =>
        {
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(version.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task PostDraft_WithTrustDisabled_IgnoresSpoofedPrincipalAndRecordsLocalFallback()
    {
        using MonitoringRuleApiHost host = new();
        host.SeedRule();
        host.Client.DefaultRequestHeaders.Add(PrincipalHeader, EncodePrincipal(
            ("http://schemas.microsoft.com/identity/claims/objectidentifier", ObjectId)));
        string token = await host.GetTokenAsync();

        HttpResponseMessage response = await host.SendAsync(
            HttpMethod.Post,
            Path("/drafts"),
            new MonitoringRuleDraftRequest("local draft", null, DefinitionDto(110m)),
            token);
        MonitoringRuleDraftsResponse drafts = await host.GetDraftsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(drafts.Drafts[0].CreatedBy, Is.EqualTo("unverified-local-caller"));
        });
    }

    private static string EncodePrincipal(params (string Type, string Value)[] claims)
    {
        string claimsJson = string.Join(
            ",",
            claims.Select(claim => $$"""{"typ":"{{claim.Type}}","val":"{{claim.Value}}"}"""));
        string json = $$"""
            {"auth_typ":"aad","claims":[{{claimsJson}}],"name_typ":"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name","role_typ":"http://schemas.microsoft.com/ws/2008/06/identity/claims/role"}
            """;

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
